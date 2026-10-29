using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Calculations;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Telemetry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

[ApiController]
[Authorize]
public sealed class TelemetryController(
    ITelemetryStore telemetryStore, IDeviceRepository devices, IDeviceTypeRepository deviceTypes, ICalculationReader calculations,
    TimeProvider clock) : BmsControllerBase
{
    private static readonly IReadOnlyDictionary<string, double> NoFields = new Dictionary<string, double>();

    /// <summary>
    /// The parameter keys a device's type defines now. Readings keep every
    /// field they were stored with, so a parameter deleted from the type still
    /// sits in older readings; these keys are what is shown, so a deleted
    /// parameter disappears from tables and charts straight away.
    /// </summary>
    private async Task<HashSet<string>> DefinedKeysAsync(Guid deviceTypeId, CancellationToken ct) =>
        ((await deviceTypes.GetByIdAsync(deviceTypeId, ct))?.Parameters ?? [])
            .Select(p => p.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, double> OnlyDefined(IReadOnlyDictionary<string, double> fields, HashSet<string> keys) =>
        fields.Where(f => keys.Contains(f.Key)).ToDictionary(f => f.Key, f => f.Value, StringComparer.OrdinalIgnoreCase);

    private static TelemetryPoint? OnlyDefined(TelemetryPoint? point, HashSet<string> keys) =>
        point is null ? null : point with { Fields = OnlyDefined(point.Fields, keys) };

    [HttpGet("api/telemetry/latest")]
    public async Task<ActionResult<IReadOnlyList<LatestTelemetryDto>>> Latest([FromQuery] Guid? tenantId, [FromQuery] Guid? siteId, CancellationToken ct)
    {
        var effectiveTenantId = EffectiveTenantFilter(tenantId);
        var filter = new DeviceFilter { TenantId = effectiveTenantId, SiteId = siteId, PageSize = 200 };
        var deviceList = await devices.ListAsync(filter, ct);
        var calculated = await calculations.GetLatestAsync(deviceList.Items.Select(d => d.Id).ToList(), ct);

        var results = new List<LatestTelemetryDto>();
        var keysByType = new Dictionary<Guid, HashSet<string>>();
        foreach (var device in deviceList.Items)
        {
            if (!keysByType.TryGetValue(device.DeviceTypeId, out var keys))
                keysByType[device.DeviceTypeId] = keys = await DefinedKeysAsync(device.DeviceTypeId, ct);
            var point = OnlyDefined(await telemetryStore.GetLatestAsync(device.Id, ct), keys);
            results.Add(new LatestTelemetryDto(device.Id, Map(point), MapCalculated(calculated, device.Id)));
        }

        return Ok(results);
    }

    [HttpGet("api/devices/{deviceId:guid}/telemetry")]
    public async Task<ActionResult<LatestTelemetryDto>> LatestForDevice(Guid deviceId, CancellationToken ct)
    {
        var device = await devices.GetByIdAsync(deviceId, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        RequireTenantAccess(device.TenantId);
        var point = OnlyDefined(await telemetryStore.GetLatestAsync(deviceId, ct), await DefinedKeysAsync(device.DeviceTypeId, ct));
        var calculated = await calculations.GetLatestAsync([deviceId], ct);
        return Ok(new LatestTelemetryDto(deviceId, Map(point), MapCalculated(calculated, deviceId)));
    }

    [HttpGet("api/devices/{deviceId:guid}/telemetry/history")]
    public async Task<ActionResult<IReadOnlyList<TelemetryPointDto>>> History(
        Guid deviceId, [FromQuery] TelemetryRange range = TelemetryRange.OneHour,
        [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var device = await devices.GetByIdAsync(deviceId, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        RequireTenantAccess(device.TenantId);

        var absolute = from is not null && to is not null;
        var points = absolute
            ? await telemetryStore.GetHistoryAsync(deviceId, from!.Value, to!.Value, ct)
            : await telemetryStore.GetHistoryAsync(deviceId, range, ct);
        var derived = absolute
            ? await calculations.GetHistoryAsync(device, from!.Value, to!.Value, ct)
            : await calculations.GetHistoryAsync(device, range, ct);

        // A reading left with no defined field (it only carried deleted
        // parameters) is dropped rather than shown as an empty row.
        var keys = await DefinedKeysAsync(device.DeviceTypeId, ct);
        var shown = points.Select(p => OnlyDefined(p, keys)!).Where(p => p.Fields.Count > 0).ToList();

        return Ok(Merge(shown, derived));
    }

    [HttpGet("api/devices/{deviceId:guid}/telemetry/rollup")]
    public async Task<ActionResult<IReadOnlyList<TelemetryRollupPointDto>>> Rollup(
        Guid deviceId, [FromQuery] RollupGranularity granularity = RollupGranularity.Daily, CancellationToken ct = default)
    {
        var device = await devices.GetByIdAsync(deviceId, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        RequireTenantAccess(device.TenantId);

        var points = await telemetryStore.GetRollupAsync(deviceId, granularity, ct);
        var keys = await DefinedKeysAsync(device.DeviceTypeId, ct);

        // Same span and window as the measured rollup (ITelemetryStore.GetRollupAsync).
        var (span, every) = granularity == RollupGranularity.Weekly ? (TimeSpan.FromDays(84), "1w") : (TimeSpan.FromDays(30), "1d");
        var to = clock.GetUtcNow();
        var derived = await calculations.GetRollupAsync(device, to - span, to, every, ct);

        var rows = new SortedDictionary<DateTimeOffset, (Dictionary<string, TelemetryFieldStatsDto> Fields, Dictionary<string, CalculatedStatsDto> Derived)>();
        (Dictionary<string, TelemetryFieldStatsDto> Fields, Dictionary<string, CalculatedStatsDto> Derived) Row(DateTimeOffset at) =>
            rows.TryGetValue(at, out var row)
                ? row
                : rows[at] = (new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase));

        foreach (var p in points)
        {
            var (fields, _) = Row(p.PeriodStart);
            foreach (var (key, stats) in p.Fields.Where(f => keys.Contains(f.Key)))
                fields[key] = new TelemetryFieldStatsDto(stats.Min, stats.Mean, stats.Max);
        }

        // The first period is partial and labelled by the range start, which
        // the measured query takes from InfluxDB's clock — a few ms off ours.
        // A calculated period starting before the first measured one is that
        // same partial period, so it lands on the measured row.
        var firstMeasured = points.Count > 0 ? points.Min(p => p.PeriodStart) : (DateTimeOffset?)null;
        foreach (var (key, series) in derived)
        {
            foreach (var p in series)
            {
                var at = firstMeasured is { } first && p.PeriodStart < first && first - p.PeriodStart < TimeSpan.FromMinutes(1) ? first : p.PeriodStart;
                Row(at).Derived[key] = new CalculatedStatsDto(p.Min, p.Mean, p.Max, p.Total);
            }
        }

        return Ok(rows.Select(r => new TelemetryRollupPointDto(r.Key, r.Value.Fields, r.Value.Derived.Count > 0 ? r.Value.Derived : null)).ToList());
    }

    /// <summary>
    /// Raw and derived values share a point wherever they share a timestamp — a
    /// delta is stamped with the reading that produced it, and windowed history
    /// buckets both onto the same window boundaries — but they stay in separate
    /// maps, so a consumer can always tell a measured value from a calculated one.
    /// </summary>
    private static List<TelemetryPointDto> Merge(
        IReadOnlyList<TelemetryPoint> points, IReadOnlyDictionary<string, IReadOnlyList<CalculatedPoint>> derived)
    {
        var rows = new SortedDictionary<DateTimeOffset, (IReadOnlyDictionary<string, double> Fields, Dictionary<string, double>? Derived)>();
        foreach (var point in points)
            rows[point.Timestamp] = (point.Fields, null);

        foreach (var (outputKey, series) in derived)
        {
            foreach (var value in series)
            {
                var row = rows.TryGetValue(value.Timestamp, out var existing) ? existing : (NoFields, null);
                row.Derived ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                row.Derived[outputKey] = value.Value;
                rows[value.Timestamp] = row;
            }
        }

        return rows.Select(r => new TelemetryPointDto(r.Key, r.Value.Fields, r.Value.Derived)).ToList();
    }

    private static IReadOnlyList<LatestCalculatedValueDto>? MapCalculated(
        IReadOnlyDictionary<Guid, IReadOnlyList<LatestCalculatedValue>> calculated, Guid deviceId) =>
        calculated.TryGetValue(deviceId, out var values)
            ? values.Select(v => new LatestCalculatedValueDto(v.RuleId, v.OutputKey, v.OutputName, v.OutputUnit, v.Value, v.Timestamp)).ToList()
            : null;

    private static TelemetryPointDto? Map(TelemetryPoint? p) => p is null ? null : new TelemetryPointDto(p.Timestamp, p.Fields);
}
