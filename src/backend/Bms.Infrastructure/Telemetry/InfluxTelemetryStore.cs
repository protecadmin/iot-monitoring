using System.Globalization;
using Bms.Application.Abstractions.Calculations;
using Bms.Application.Abstractions.Telemetry;
using Bms.Domain.Calculations;
using Bms.Domain.Telemetry;
using InfluxDB.Client;
using InfluxDB.Client.Writes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bms.Infrastructure.Telemetry;

/// <summary>
/// The only place the rest of the system talks to InfluxDB — see
/// docs/database/influxdb.md. Measurement "telemetry", tagged by
/// tenantId/siteId/deviceId, one point per reading, timestamped at event
/// time (NormalizedTelemetry.Timestamp), with ingestion time carried
/// alongside as a field so freshness/latency can be queried without a
/// second write path.
/// </summary>
public sealed class InfluxTelemetryStore : ITelemetryStore, ICalculatedSeriesStore, IDisposable
{
    private const string Measurement = "telemetry";

    private readonly InfluxDBClient _client;
    private readonly InfluxDbOptions _options;
    private readonly ILogger<InfluxTelemetryStore> _logger;

    public InfluxTelemetryStore(IOptions<InfluxDbOptions> options, ILogger<InfluxTelemetryStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new InfluxDBClient(new InfluxDBClientOptions(_options.Url)
        {
            Token = _options.Token,
            Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds))
        });
    }

    public async Task WriteAsync(NormalizedTelemetry telemetry, CancellationToken ct)
    {
        var point = PointData.Measurement(Measurement)
            .Tag("tenantId", telemetry.TenantId.ToString())
            .Tag("siteId", telemetry.SiteId.ToString())
            .Tag("deviceId", telemetry.DeviceId.ToString())
            .Timestamp(telemetry.Timestamp.UtcDateTime, InfluxDB.Client.Api.Domain.WritePrecision.Ms);

        // Field names are whatever the device's Device Type defines them
        // to be (see IngestionPipeline) — InfluxDB is schemaless per point,
        // so no fixed field list is needed here.
        foreach (var (key, value) in telemetry.Fields)
            point = point.Field(key, value);
        point = point.Field("ingestionLatencyMs", (telemetry.IngestedAt - telemetry.Timestamp).TotalMilliseconds);

        try
        {
            var writeApi = _client.GetWriteApiAsync();
            await writeApi.WritePointAsync(point, _options.Bucket, _options.Org, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed writing telemetry point for device {DeviceId} to InfluxDB", telemetry.DeviceId);
            throw;
        }
    }

    public async Task<TelemetryPoint?> GetLatestAsync(Guid deviceId, CancellationToken ct)
    {
        var flux = $"""
            from(bucket: "{_options.Bucket}")
              |> range(start: -30d)
              |> filter(fn: (r) => r._measurement == "{Measurement}" and r.deviceId == "{deviceId}")
              |> filter(fn: (r) => r._field != "ingestionLatencyMs")
              |> last()
            """;

        try
        {
            var tables = await _client.GetQueryApi().QueryAsync(flux, _options.Org, ct);
            var points = ToPoints(tables);
            return points.OrderByDescending(p => p.Timestamp).FirstOrDefault();
        }
        catch (Exception ex)
        {
            // A read-side InfluxDB outage should degrade the dashboard to
            // "no data yet", not take down the whole request — the same
            // detect/log/continue philosophy WriteAsync already applies on
            // the write side (core.txt section 50: "InfluxDB unavailable").
            _logger.LogError(ex, "Failed querying latest telemetry for device {DeviceId} from InfluxDB", deviceId);
            return null;
        }
    }

    public Task<IReadOnlyList<TelemetryPoint>> GetHistoryAsync(Guid deviceId, TelemetryRange range, CancellationToken ct)
    {
        // Shared with derived history (TelemetryRangeWindows), so a calculated
        // series buckets onto exactly the same windows as the raw one it is shown beside.
        var window = TelemetryRangeWindows.Resolve(range);
        return QueryRangeAsync(deviceId, $"-{(long)window.Duration.TotalSeconds}s", "now()", window.AggregateEvery, ct);
    }

    public Task<IReadOnlyList<TelemetryPoint>> GetHistoryAsync(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        QueryRangeAsync(deviceId, from.UtcDateTime.ToString("O"), to.UtcDateTime.ToString("O"), null, ct);

    private async Task<IReadOnlyList<TelemetryPoint>> QueryRangeAsync(Guid deviceId, string start, string stop, string? aggregateEvery, CancellationToken ct)
    {
        var aggregation = aggregateEvery is null
            ? string.Empty
            : $"""|> aggregateWindow(every: {aggregateEvery}, fn: mean, createEmpty: false)""";

        var flux = $"""
            from(bucket: "{_options.Bucket}")
              |> range(start: {start}, stop: {stop})
              |> filter(fn: (r) => r._measurement == "{Measurement}" and r.deviceId == "{deviceId}")
              |> filter(fn: (r) => r._field != "ingestionLatencyMs")
              {aggregation}
              |> sort(columns: ["_time"])
            """;

        try
        {
            var tables = await _client.GetQueryApi().QueryAsync(flux, _options.Org, ct);
            return ToPoints(tables).OrderBy(p => p.Timestamp).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed querying telemetry history for device {DeviceId} from InfluxDB", deviceId);
            return [];
        }
    }

    public async Task<IReadOnlyList<TelemetryRollupPoint>> GetRollupAsync(Guid deviceId, RollupGranularity granularity, CancellationToken ct)
    {
        var (start, every) = granularity switch
        {
            RollupGranularity.Daily => ("-30d", "1d"),
            RollupGranularity.Weekly => ("-12w", "1w"),
            _ => ("-30d", "1d")
        };

        try
        {
            // A single fixed "now" shared by all three queries below — using
            // the Flux literal now() in each query separately let InfluxDB
            // evaluate it independently per HTTP request, so the three
            // concurrent queries' still-forming final (today's/this week's)
            // window boundaries landed a few milliseconds apart. Since
            // ToPoints keys off the exact window timestamp, that split what
            // should have been one row into several near-duplicate rows —
            // each only populated with whichever stat happened to key-match.
            // .UtcDateTime.ToString("O"), not DateTimeOffset.ToString("O") directly —
            // matches GetHistoryAsync's absolute-range overload below exactly; a
            // DateTimeOffset's "O" format always appends an explicit "+00:00" rather
            // than "Z", and only the DateTime (Kind=Utc) form is already proven to
            // parse correctly as a Flux range boundary.
            var stop = DateTimeOffset.UtcNow.UtcDateTime.ToString("O");

            // Flux has no single aggregation that returns min/mean/max together per
            // window, so this runs the same aggregateWindow three times (one per
            // statistic) and merges the three point-sets client-side by (time, field)
            // — straightforward given GetHistoryAsync already has the single-stat
            // query shape and ToPoints merge-by-time logic to reuse.
            var minTask = QueryAggregatedRangeAsync(deviceId, start, stop, every, "min", ct);
            var meanTask = QueryAggregatedRangeAsync(deviceId, start, stop, every, "mean", ct);
            var maxTask = QueryAggregatedRangeAsync(deviceId, start, stop, every, "max", ct);
            await Task.WhenAll(minTask, meanTask, maxTask);

            var byTime = new SortedDictionary<DateTimeOffset, Dictionary<string, (double? Min, double? Mean, double? Max)>>();

            void Merge(IReadOnlyList<TelemetryPoint> points, Func<(double? Min, double? Mean, double? Max), double, (double?, double?, double?)> update)
            {
                foreach (var point in points)
                {
                    if (!byTime.TryGetValue(point.Timestamp, out var fields))
                    {
                        fields = new Dictionary<string, (double?, double?, double?)>();
                        byTime[point.Timestamp] = fields;
                    }
                    foreach (var (key, value) in point.Fields)
                        fields[key] = update(fields.GetValueOrDefault(key), value);
                }
            }

            Merge(minTask.Result, (existing, v) => (v, existing.Mean, existing.Max));
            Merge(meanTask.Result, (existing, v) => (existing.Min, v, existing.Max));
            Merge(maxTask.Result, (existing, v) => (existing.Min, existing.Mean, v));

            return byTime.Select(kv => new TelemetryRollupPoint(
                kv.Key,
                kv.Value
                    .Where(f => f.Value is { Min: not null, Mean: not null, Max: not null })
                    .ToDictionary(f => f.Key, f => new TelemetryFieldStats(f.Value.Min!.Value, f.Value.Mean!.Value, f.Value.Max!.Value))
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed querying telemetry rollup for device {DeviceId} from InfluxDB", deviceId);
            return [];
        }
    }

    private async Task<IReadOnlyList<TelemetryPoint>> QueryAggregatedRangeAsync(Guid deviceId, string start, string stop, string aggregateEvery, string fn, CancellationToken ct)
    {
        var flux = $"""
            from(bucket: "{_options.Bucket}")
              |> range(start: {start}, stop: {stop})
              |> filter(fn: (r) => r._measurement == "{Measurement}" and r.deviceId == "{deviceId}")
              |> filter(fn: (r) => r._field != "ingestionLatencyMs")
              |> aggregateWindow(every: {aggregateEvery}, fn: {fn}, createEmpty: false, timeSrc: "_start")
              |> sort(columns: ["_time"])
            """;

        var tables = await _client.GetQueryApi().QueryAsync(flux, _options.Org, ct);
        return ToPoints(tables).OrderBy(p => p.Timestamp).ToList();
    }

    public async Task<IReadOnlyList<CalculatedPoint>> GetCounterDeltasAsync(
        Guid deviceId, string counterField, DateTimeOffset from, DateTimeOffset to,
        double? maxDelta, IReadOnlyList<DateTimeOffset> resets, string? aggregateEvery, bool timestampAtWindowStart, CancellationToken ct)
    {
        static string Time(DateTimeOffset t) => t.UtcDateTime.ToString("O");

        // The reading just before the range gives the first in-range reading its
        // delta. The 30-day lookback matches GetLatestAsync's notion of "the
        // previous reading", and last() keeps that scan cheap. It never reaches
        // back past a reset: a reading on the old basis can't pair with one on
        // the new.
        var lookbackFrom = from - TimeSpan.FromDays(30);
        var lastResetBefore = resets.Where(r => r <= from).DefaultIfEmpty(DateTimeOffset.MinValue).Max();
        if (lastResetBefore > lookbackFrom) lookbackFrom = lastResetBefore;
        var lookback = Time(lookbackFrom);
        var start = Time(from);
        var field = EscapeFluxString(counterField);

        // Resets inside the range split it into segments that are differenced
        // separately, so the first reading of each later segment has no pair
        // and yields nothing — exactly like the live TotalizerDeltaHandler.
        var bounds = new List<DateTimeOffset> { from };
        bounds.AddRange(resets.Where(r => r > from && r < to).OrderBy(r => r));
        bounds.Add(to);
        var segments = new List<string>();
        for (var i = 0; i < bounds.Count - 1; i++)
        {
            var segStart = Time(bounds[i]);
            var segStop = Time(bounds[i + 1]);
            var source = i == 0 && lookbackFrom < from
                ? $"union(tables: [counter(start: {lookback}, stop: {start}) |> last(), counter(start: {segStart}, stop: {segStop})])"
                : $"counter(start: {segStart}, stop: {segStop})";
            segments.Add($"""
                seg{i} = {source}
                  |> group(columns: ["_field"])
                  |> sort(columns: ["_time"])
                  |> difference(nonNegative: false)
                  |> filter(fn: (r) => r._time >= {segStart})
                """);
        }
        var combined = segments.Count == 1 ? "seg0" : $"union(tables: [{string.Join(", ", Enumerable.Range(0, segments.Count).Select(i => $"seg{i}"))}])";

        // Always written with a decimal point: Flux will not compare an integer
        // literal against the float _value.
        var maxDeltaFilter = maxDelta is { } max
            ? $" and r._value <= {max.ToString("0.0###############", CultureInfo.InvariantCulture)}"
            : string.Empty;

        // sum, never mean — consumption over a window is the total of its deltas.
        var aggregation = aggregateEvery is null
            ? string.Empty
            : timestampAtWindowStart
                ? $"""|> aggregateWindow(every: {aggregateEvery}, fn: sum, createEmpty: false, timeSrc: "_start")"""
                : $"""|> aggregateWindow(every: {aggregateEvery}, fn: sum, createEmpty: false)""";

        // Mirrors Bms.Domain.Calculations.TotalizerDelta: difference() pairs each
        // reading with the one before it in time order (no fixed interval is
        // assumed); the first reading has no pair and yields nothing; a negative
        // delta is a reset and is dropped; a delta above MaxDelta is dropped; a
        // zero delta is kept. Raw telemetry is only read, never rewritten.
        var flux = $"""
            counter = (start, stop) => from(bucket: "{_options.Bucket}")
              |> range(start: start, stop: stop)
              |> filter(fn: (r) => r._measurement == "{Measurement}" and r.deviceId == "{deviceId}" and r._field == "{field}")
              |> keep(columns: ["_time", "_value", "_field"])

            {string.Join("\n\n", segments)}

            {combined}
              |> group(columns: ["_field"])
              |> sort(columns: ["_time"])
              |> filter(fn: (r) => r._value >= 0.0{maxDeltaFilter})
              {aggregation}
              |> sort(columns: ["_time"])
            """;

        try
        {
            var tables = await _client.GetQueryApi().QueryAsync(flux, _options.Org, ct);
            return tables
                .SelectMany(t => t.Records)
                .Where(r => r.GetTime() is not null && r.GetValue() is double)
                .Select(r => new CalculatedPoint(new DateTimeOffset(r.GetTime()!.Value.ToDateTimeUtc(), TimeSpan.Zero), (double)r.GetValue()))
                .OrderBy(p => p.Timestamp)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed querying counter deltas of {Field} for device {DeviceId} from InfluxDB", counterField, deviceId);
            return [];
        }
    }

    public async Task<IReadOnlyList<CalculatedPoint>> GetFormulaSeriesAsync(
        Guid deviceId, FormulaExpression formula, string outputField, DateTimeOffset from, DateTimeOffset to,
        string? aggregateEvery, bool timestampAtWindowStart, CancellationToken ct, string aggregateFn = "mean")
    {
        static string Time(DateTimeOffset t) => t.UtcDateTime.ToString("O");
        static string Column(string field) => $"r[\"{EscapeFluxString(field)}\"]";

        // One row per reading with every field side by side, so the formula
        // can read them together exactly as the live handler does.
        var fields = formula.Variables.Append(outputField).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var fieldFilter = string.Join(" or ", fields.Select(f => $"r._field == \"{EscapeFluxString(f)}\""));
        var hasInputs = string.Join(" and ", formula.Variables.Select(v => $"exists {Column(v)}"));

        // Concatenated rather than interpolated: Flux's record braces can't sit
        // inside a single-$ raw string.
        var mapToValue = "map(fn: (r) => ({_time: r._time, _value: float(v: " + formula.Render(Column) + ")}))";

        // mean (or min/max for reports), never sum — a formula yields a
        // level (kW, %), and summing levels over a window means nothing.
        if (aggregateFn is not ("mean" or "min" or "max"))
            throw new ArgumentOutOfRangeException(nameof(aggregateFn), aggregateFn, "Use mean, min or max");
        var aggregation = aggregateEvery is null
            ? string.Empty
            : timestampAtWindowStart
                ? $"""|> aggregateWindow(every: {aggregateEvery}, fn: {aggregateFn}, createEmpty: false, timeSrc: "_start")"""
                : $"""|> aggregateWindow(every: {aggregateEvery}, fn: {aggregateFn}, createEmpty: false)""";

        var flux = $"""
            from(bucket: "{_options.Bucket}")
              |> range(start: {Time(from)}, stop: {Time(to)})
              |> filter(fn: (r) => r._measurement == "{Measurement}" and r.deviceId == "{deviceId}" and ({fieldFilter}))
              |> keep(columns: ["_time", "_value", "_field"])
              |> pivot(rowKey: ["_time"], columnKey: ["_field"], valueColumn: "_value")
              |> filter(fn: (r) => {hasInputs} and not exists {Column(outputField)})
              |> {mapToValue}
              |> group()
              |> sort(columns: ["_time"])
              {aggregation}
              |> sort(columns: ["_time"])
            """;

        try
        {
            var tables = await _client.GetQueryApi().QueryAsync(flux, _options.Org, ct);
            return tables
                .SelectMany(t => t.Records)
                .Where(r => r.GetTime() is not null && r.GetValue() is double v && double.IsFinite(v))
                .Select(r => new CalculatedPoint(new DateTimeOffset(r.GetTime()!.Value.ToDateTimeUtc(), TimeSpan.Zero), (double)r.GetValue()))
                .OrderBy(p => p.Timestamp)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed querying formula {Formula} for device {DeviceId} from InfluxDB", formula.Text, deviceId);
            return [];
        }
    }

    public async Task<IReadOnlyList<CalculatedPoint>> GetWindowedLevelSeriesAsync(
        Guid deviceId, string sourceField, LevelAggregation aggregation, DateTimeOffset from, DateTimeOffset to,
        string windowEvery, string? rollupEvery, string rollupFn, bool timestampAtWindowStart, CancellationToken ct)
    {
        static string Time(DateTimeOffset t) => t.UtcDateTime.ToString("O");

        if (rollupFn is not ("mean" or "min" or "max"))
            throw new ArgumentOutOfRangeException(nameof(rollupFn), rollupFn, "Use mean, min or max");

        var field = EscapeFluxString(sourceField);
        var decibel = aggregation == LevelAggregation.EnergyDecibel;

        // Flux clips its last window to the range stop and labels it with that
        // clipped time, so a range ending mid-window would return the window
        // still being filled, stamped "now" and holding a part-period average.
        // Widening the range to whole periods keeps every label a true
        // boundary; the period still open at `to` is then dropped below, so
        // history shows exactly the windows the live handler would publish.
        var grain = GrainSeconds(rollupEvery ?? windowEvery);
        var queryFrom = grain > 0 ? LevelWindow.StartOf(from, grain) : from;
        var queryTo = grain > 0 ? LevelWindow.EndOf(to, grain) : to;

        // Leq averages sound energy, not decibels, so readings are taken into
        // the energy domain before any averaging and brought back after the
        // last one. min and max survive the round trip either way — log10 is
        // monotonic, so the quietest window in energy is the quietest in dB.
        var toEnergy = decibel
            ? """|> map(fn: (r) => ({r with _value: math.pow(x: 10.0, y: r._value / 10.0)}))"""
            : string.Empty;
        var toDecibels = decibel
            ? """|> map(fn: (r) => ({r with _value: 10.0 * math.log10(x: r._value)}))"""
            : string.Empty;

        // The averaging window is always labelled by its start, so a window
        // ending exactly on a rollup boundary is counted in the period it
        // belongs to rather than the next one. Only the outermost stage
        // follows the caller's choice of label.
        var windowLabel = rollupEvery is null && !timestampAtWindowStart ? string.Empty : ", timeSrc: \"_start\"";
        var rollup = rollupEvery is null
            ? string.Empty
            : timestampAtWindowStart
                ? $"""|> aggregateWindow(every: {rollupEvery}, fn: {rollupFn}, createEmpty: false, timeSrc: "_start")"""
                : $"""|> aggregateWindow(every: {rollupEvery}, fn: {rollupFn}, createEmpty: false)""";

        var flux = $"""
            import "math"

            from(bucket: "{_options.Bucket}")
              |> range(start: {Time(queryFrom)}, stop: {Time(queryTo)})
              |> filter(fn: (r) => r._measurement == "{Measurement}" and r.deviceId == "{deviceId}" and r._field == "{field}")
              |> keep(columns: ["_time", "_value"])
              |> group()
              |> sort(columns: ["_time"])
              {toEnergy}
              |> aggregateWindow(every: {windowEvery}, fn: mean, createEmpty: false{windowLabel})
              {rollup}
              {toDecibels}
              |> sort(columns: ["_time"])
            """;

        try
        {
            var tables = await _client.GetQueryApi().QueryAsync(flux, _options.Org, ct);
            return tables
                .SelectMany(t => t.Records)
                .Where(r => r.GetTime() is not null && r.GetValue() is double v && double.IsFinite(v))
                .Select(r => new CalculatedPoint(new DateTimeOffset(r.GetTime()!.Value.ToDateTimeUtc(), TimeSpan.Zero), (double)r.GetValue()))
                // A period reaching past what was asked for has not finished,
                // so it is not yet an average of anything.
                .Where(p => grain <= 0 || PeriodEnd(p.Timestamp, grain, timestampAtWindowStart) <= to)
                .OrderBy(p => p.Timestamp)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed querying {Aggregation} of {Field} over {Window} windows for device {DeviceId} from InfluxDB",
                aggregation, sourceField, windowEvery, deviceId);
            return [];
        }
    }

    /// <summary>A period's length in seconds, or 0 when the duration can't be read and the range is left as asked for.</summary>
    private static int GrainSeconds(string duration) => LevelWindow.TryParseFluxSeconds(duration, out var seconds) ? seconds : 0;

    /// <summary>When a point's period ends, given which end of it the point is labelled by.</summary>
    private static DateTimeOffset PeriodEnd(DateTimeOffset label, int grain, bool labelledAtStart) =>
        labelledAtStart ? label.AddSeconds(grain) : label;

    /// <summary>
    /// Parameter keys are user-defined — one existing key already contains
    /// spaces — so they are escaped rather than trusted inside a Flux string
    /// literal, including Flux's ${...} interpolation.
    /// </summary>
    private static string EscapeFluxString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$");

    private static List<TelemetryPoint> ToPoints(List<InfluxDB.Client.Core.Flux.Domain.FluxTable> tables)
    {
        var byTime = new Dictionary<DateTime, Dictionary<string, double>>();

        foreach (var table in tables)
        {
            foreach (var record in table.Records)
            {
                var time = record.GetTime()?.ToDateTimeUtc() ?? DateTime.UtcNow;
                var field = record.GetField();
                if (field is null || record.GetValue() is not double value)
                    continue;

                if (!byTime.TryGetValue(time, out var fields))
                {
                    fields = new Dictionary<string, double>();
                    byTime[time] = fields;
                }

                fields[field] = value;
            }
        }

        return byTime.Select(kv => new TelemetryPoint(new DateTimeOffset(kv.Key, TimeSpan.Zero), kv.Value)).ToList();
    }

    public void Dispose() => _client.Dispose();
}
