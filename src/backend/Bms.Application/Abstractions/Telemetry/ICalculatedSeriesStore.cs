using Bms.Application.Abstractions.Calculations;
using Bms.Domain.Calculations;

namespace Bms.Application.Abstractions.Telemetry;

/// <summary>
/// Time-series primitives for derived values. They are computed on read from
/// raw telemetry rather than written as a second copy, so they cover history
/// recorded before a rule existed and never duplicate the data they come from.
/// </summary>
public interface ICalculatedSeriesStore
{
    /// <summary>
    /// Consumption between consecutive readings of a counter field, following
    /// Bms.Domain.Calculations.TotalizerDelta: negative deltas (resets) and
    /// deltas above <paramref name="maxDelta"/> are dropped, zero deltas kept.
    /// The last reading before <paramref name="from"/> is included so the first
    /// in-range reading still gets its delta, and readings are paired by time
    /// order — not assumed to arrive at a fixed interval.
    /// </summary>
    /// <param name="resets">Counter reset points (see CounterReset), oldest first. No delta is computed across one — the first reading after it is only a new baseline.</param>
    /// <param name="aggregateEvery">Flux duration ("5m", "1h", "1d", "1mo"). Deltas are summed per window — never averaged. Null returns one point per reading.</param>
    /// <param name="timestampAtWindowStart">Label windows by their start (reports) or by their end, which is what raw telemetry history uses (so the two merge by time).</param>
    Task<IReadOnlyList<CalculatedPoint>> GetCounterDeltasAsync(
        Guid deviceId,
        string counterField,
        DateTimeOffset from,
        DateTimeOffset to,
        double? maxDelta,
        IReadOnlyList<DateTimeOffset> resets,
        string? aggregateEvery,
        bool timestampAtWindowStart,
        CancellationToken ct);

    /// <summary>
    /// A formula's value for every reading that carries all of its inputs.
    /// A reading that already carries <paramref name="outputField"/> — the
    /// device measured the value itself — is skipped, so a measured value is
    /// never shadowed by a calculated one.
    /// </summary>
    /// <param name="aggregateEvery">Flux duration ("5m", "1h", "1d", "1mo"). Values are averaged per window — a formula yields a rate or level, not a quantity to add up. Null returns one point per reading.</param>
    /// <param name="timestampAtWindowStart">As for <see cref="GetCounterDeltasAsync"/>.</param>
    /// <param name="aggregateFn">Flux aggregate per window — "mean" by default; "min"/"max" for report periods.</param>
    Task<IReadOnlyList<CalculatedPoint>> GetFormulaSeriesAsync(
        Guid deviceId,
        FormulaExpression formula,
        string outputField,
        DateTimeOffset from,
        DateTimeOffset to,
        string? aggregateEvery,
        bool timestampAtWindowStart,
        CancellationToken ct,
        string aggregateFn = "mean");

    /// <summary>
    /// A level averaged over fixed windows — one point per window, aligned to
    /// the Unix epoch (see Bms.Domain.Calculations.LevelWindow). This is the
    /// only implementation of the averaging: the live handler asks for the one
    /// window that just completed and history asks for a range of them, so a
    /// published value and the same period read back can never disagree.
    /// </summary>
    /// <param name="aggregation">Arithmetic for a plain mean, EnergyDecibel for Leq.</param>
    /// <param name="windowEvery">Flux duration of one averaging window ("300s") — the rule's window, not a display granularity.</param>
    /// <param name="rollupEvery">
    /// An optional second stage that combines whole windows into longer periods,
    /// for a coarse history view or a report. Equal-length windows combine
    /// correctly: the mean of window means is the overall mean, and in the
    /// energy domain the same holds for Leq. Null returns one point per window.
    /// </param>
    /// <param name="rollupFn">Flux aggregate for that second stage — "mean", "min" or "max". Ignored when <paramref name="rollupEvery"/> is null.</param>
    /// <param name="timestampAtWindowStart">
    /// Label the points by their period's start (reports) or its end, which is
    /// what raw telemetry history uses so the two merge by time. Windows inside
    /// a rollup are always labelled by their start, so one never lands in the
    /// following period.
    /// </param>
    Task<IReadOnlyList<CalculatedPoint>> GetWindowedLevelSeriesAsync(
        Guid deviceId,
        string sourceField,
        LevelAggregation aggregation,
        DateTimeOffset from,
        DateTimeOffset to,
        string windowEvery,
        string? rollupEvery,
        string rollupFn,
        bool timestampAtWindowStart,
        CancellationToken ct);
}
