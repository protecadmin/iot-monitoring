using Bms.Application.Abstractions.Calculations;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Telemetry;
using Bms.Domain;
using Bms.Domain.Calculations;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;

namespace Bms.Calculations.Handlers;

/// <summary>
/// A level averaged over a fixed window, for the readings whose limits are
/// written against a period rather than a moment — an 8-hour noise exposure,
/// a WBGT average, a 5-minute boundary noise level.
///
/// A value is published once per window, when a reading arrives that belongs
/// to a later one, rather than on every reading: the window it closes is the
/// first one whose average is actually final. So a device reporting every 5
/// seconds against a 5-minute window causes one query per 60 readings, and
/// the alert it feeds fires once per period instead of once per spike.
///
/// <see cref="CalculationState"/> holds the window being filled —
/// LastSourceTimestamp is the newest reading seen, and the window containing
/// it is the open one. Advancing it is a compare-and-swap, so two messages
/// racing across the same boundary can only publish that window once.
/// </summary>
public abstract class WindowedLevelHandler(ICalculationStateRepository states, ICalculatedSeriesStore series) : ICalculationHandler
{
    public abstract CalculationType Type { get; }

    /// <summary>How readings combine over the window. The one thing that differs between the windowed types.</summary>
    protected abstract LevelAggregation Aggregation { get; }

    public string SourceRequirement => "an Instantaneous parameter (a measured level)";

    // A Counter only grows and a Digital is a state, so neither has a
    // meaningful average; a Derived value isn't in the reading this runs
    // against, exactly as for a formula's inputs.
    public bool IsEligibleSource(DeviceTypeParameter parameter) => parameter.Kind == ParameterKind.Instantaneous;

    public IReadOnlyList<string> InputKeys(CalculationRule rule) =>
        string.IsNullOrWhiteSpace(rule.SourceParameterKey) ? [] : [rule.SourceParameterKey];

    public async Task<CalculationUpdate?> EvaluateAsync(CalculationRule rule, Device device, NormalizedTelemetry telemetry, CancellationToken ct)
    {
        if (!telemetry.Fields.TryGetValue(rule.SourceParameterKey, out var current) || !double.IsFinite(current))
            return null;

        var window = WindowSeconds(rule);
        var state = await states.GetAsync(rule.Id, device.Id, ct);

        // Nothing to close yet — this reading only opens the first window.
        if (state is null)
        {
            await states.TryCreateAsync(new CalculationState
            {
                TenantId = device.TenantId,
                CalculationRuleId = rule.Id,
                DeviceId = device.Id,
                LastSourceValue = current,
                LastSourceTimestamp = telemetry.Timestamp
            }, ct);
            return null;
        }

        // A late or duplicated reading is still in history, and history is what
        // the average is computed from, so it counts there — but it must not
        // move the open window backwards.
        if (telemetry.Timestamp <= state.LastSourceTimestamp)
            return null;

        // Still inside the open window: record the reading and publish nothing.
        if (!LevelWindow.CrossesWindow(state.LastSourceTimestamp, telemetry.Timestamp, window))
        {
            await states.TryAdvanceAsync(rule.Id, device.Id, state.LastSourceTimestamp, current, telemetry.Timestamp, null, null, ct);
            return null;
        }

        var closedStart = LevelWindow.StartOf(state.LastSourceTimestamp, window);
        var closedEnd = closedStart.AddSeconds(window);
        var points = await series.GetWindowedLevelSeriesAsync(
            device.Id, rule.SourceParameterKey, Aggregation, closedStart, closedEnd,
            LevelWindow.ToFluxDuration(window), rollupEvery: null, rollupFn: "mean",
            timestampAtWindowStart: false, ct);

        // The window produced nothing — every reading in it was dropped at
        // ingestion, or the store is unreachable. Open the next one anyway;
        // the gap stays a gap rather than being folded into a later average.
        if (points is not [.., var closed])
        {
            await states.TryAdvanceAsync(rule.Id, device.Id, state.LastSourceTimestamp, current, telemetry.Timestamp, null, null, ct);
            return null;
        }

        // The value is stamped at the end of the window it describes, which is
        // also how the history of it is labelled, so the live reading and the
        // chart agree on which period it was.
        if (!await states.TryAdvanceAsync(rule.Id, device.Id, state.LastSourceTimestamp, current, telemetry.Timestamp, closed.Value, closedEnd, ct))
            return null;

        return new CalculationUpdate(
            device.TenantId, device.SiteId, device.Id, rule.Id, rule.Key,
            rule.OutputKey, rule.OutputName, rule.OutputUnit, closed.Value, closedEnd);
    }

    // A windowed average is a level (dB(A), °C): a report period is described
    // by its range and average, never a total.
    public async Task<IReadOnlyList<CalculatedRollupPoint>> RollupAsync(
        CalculationRule rule, Device device, DateTimeOffset from, DateTimeOffset to, string every, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rule.SourceParameterKey))
            return [];

        Task<IReadOnlyList<CalculatedPoint>> Series(string fn) => series.GetWindowedLevelSeriesAsync(
            device.Id, rule.SourceParameterKey, Aggregation, from, to,
            LevelWindow.ToFluxDuration(WindowSeconds(rule)), every, fn,
            timestampAtWindowStart: true, ct);

        var min = Series("min");
        var mean = Series("mean");
        var max = Series("max");
        await Task.WhenAll(min, mean, max);

        var minByPeriod = min.Result.ToDictionary(p => p.Timestamp, p => p.Value);
        var maxByPeriod = max.Result.ToDictionary(p => p.Timestamp, p => p.Value);
        return mean.Result
            .Select(p => new CalculatedRollupPoint(p.Timestamp, minByPeriod.GetValueOrDefault(p.Timestamp, p.Value), p.Value, maxByPeriod.GetValueOrDefault(p.Timestamp, p.Value), null))
            .ToList();
    }

    public Task<IReadOnlyList<CalculatedPoint>> QueryAsync(
        CalculationRule rule, Device device, DateTimeOffset from, DateTimeOffset to,
        string? aggregateEvery, bool timestampAtWindowStart, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rule.SourceParameterKey))
            return Task.FromResult<IReadOnlyList<CalculatedPoint>>([]);

        // A window is the finest this series goes: asking for 5-minute points
        // of an 8-hour average can only return the 8-hour averages, so the
        // second stage is skipped rather than producing one point per window
        // relabelled as something it isn't.
        var window = WindowSeconds(rule);
        var rollup = aggregateEvery is not null && LevelWindow.TryParseFluxSeconds(aggregateEvery, out var requested) && requested > window
            ? aggregateEvery
            : null;

        return series.GetWindowedLevelSeriesAsync(
            device.Id, rule.SourceParameterKey, Aggregation, from, to,
            LevelWindow.ToFluxDuration(window), rollup, "mean", timestampAtWindowStart, ct);
    }

    private static int WindowSeconds(CalculationRule rule) => rule.WindowSeconds ?? LevelWindow.DefaultSeconds;
}

/// <summary>
/// TIME_WEIGHTED_AVERAGE: the arithmetic mean of a level over the window — a
/// WBGT or dust average, where a limit describes a sustained condition.
/// </summary>
public sealed class TimeWeightedAverageHandler(ICalculationStateRepository states, ICalculatedSeriesStore series)
    : WindowedLevelHandler(states, series)
{
    public override CalculationType Type => CalculationType.TimeWeightedAverage;
    protected override LevelAggregation Aggregation => LevelAggregation.Arithmetic;
}

/// <summary>
/// EQUIVALENT_CONTINUOUS_LEVEL: Leq, the level of steady sound carrying the
/// same energy as the window's varying one. This is the number noise limits
/// are written against — an occupational 8-hour exposure, a 5-minute or
/// 1-hour boundary level — and it is not the arithmetic mean of the readings.
/// </summary>
public sealed class EquivalentContinuousLevelHandler(ICalculationStateRepository states, ICalculatedSeriesStore series)
    : WindowedLevelHandler(states, series)
{
    public override CalculationType Type => CalculationType.EquivalentContinuousLevel;
    protected override LevelAggregation Aggregation => LevelAggregation.EnergyDecibel;
}
