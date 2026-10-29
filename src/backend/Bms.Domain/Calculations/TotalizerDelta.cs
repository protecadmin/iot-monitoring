namespace Bms.Domain.Calculations;

public enum TotalizerTransition
{
    /// <summary>No previous reading. Establishes the baseline; produces nothing.</summary>
    FirstReading,

    /// <summary>Same value as the previous reading. Zero consumption.</summary>
    Unchanged,

    /// <summary>Grew by an accepted amount. The difference is the consumption.</summary>
    Increase,

    /// <summary>Went down — a reset, rollover or replaced meter. Starts a new accumulation period; produces nothing.</summary>
    Decrease,

    /// <summary>Grew by more than the rule's MaxDelta. Treated like a reset rather than counted.</summary>
    ExceedsMaxDelta,

    /// <summary>Not newer than the previous reading — a duplicate or late arrival. Ignored; the baseline does not move.</summary>
    NotNewer,

    /// <summary>Not a finite number. Ignored; the baseline does not move.</summary>
    Invalid
}

/// <param name="Transition">What kind of change this reading represents.</param>
/// <param name="Consumption">The derived value — present only for <see cref="TotalizerTransition.Unchanged"/> and <see cref="TotalizerTransition.Increase"/>.</param>
/// <param name="AdvancesBaseline">Whether this reading becomes the previous reading for the next one.</param>
public readonly record struct TotalizerDeltaResult(TotalizerTransition Transition, double? Consumption, bool AdvancesBaseline);

/// <summary>
/// The one definition of how consumption is derived from a cumulative counter.
///
/// Live evaluation calls this directly. The historical query in
/// InfluxTelemetryStore.GetCounterDeltasAsync applies the same rules in Flux
/// — the first reading has no delta, negative deltas and deltas above MaxDelta
/// are dropped, zero deltas are kept — so a live Energy Consumption value and
/// the same period read back from history agree. TotalizerDeltaTests pins
/// these cases down; change both together or neither.
///
/// The consumption is not rounded: 100000.21 − 100000.20 is carried as the
/// nearest double to 0.01, and rounding is left to display, so tiny per-reading
/// deltas still sum correctly over a month.
/// </summary>
public static class TotalizerDelta
{
    public static TotalizerDeltaResult Evaluate(
        double? previousValue,
        DateTimeOffset? previousTimestamp,
        double currentValue,
        DateTimeOffset currentTimestamp,
        double? maxDelta)
    {
        if (!double.IsFinite(currentValue))
            return new(TotalizerTransition.Invalid, null, AdvancesBaseline: false);

        if (previousValue is not { } previous || previousTimestamp is not { } previousAt)
            return new(TotalizerTransition.FirstReading, null, AdvancesBaseline: true);

        if (currentTimestamp <= previousAt)
            return new(TotalizerTransition.NotNewer, null, AdvancesBaseline: false);

        var delta = currentValue - previous;

        if (delta < 0)
            return new(TotalizerTransition.Decrease, null, AdvancesBaseline: true);

        if (delta == 0)
            return new(TotalizerTransition.Unchanged, 0d, AdvancesBaseline: true);

        if (maxDelta is { } max && delta > max)
            return new(TotalizerTransition.ExceedsMaxDelta, null, AdvancesBaseline: true);

        return new(TotalizerTransition.Increase, delta, AdvancesBaseline: true);
    }
}
