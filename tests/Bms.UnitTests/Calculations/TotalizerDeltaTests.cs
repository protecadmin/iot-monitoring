using Bms.Domain.Calculations;
using Xunit;

namespace Bms.UnitTests.Calculations;

public class TotalizerDeltaTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Feeds readings through the algorithm the way live ingestion does, returning every produced consumption.</summary>
    private static List<double> Run(double? maxDelta, params (int Minutes, double Value)[] readings)
    {
        double? previous = null;
        DateTimeOffset? previousAt = null;
        var produced = new List<double>();

        foreach (var (minutes, value) in readings)
        {
            var at = T0.AddMinutes(minutes);
            var result = TotalizerDelta.Evaluate(previous, previousAt, value, at, maxDelta);
            if (result.Consumption is { } c) produced.Add(c);
            if (result.AdvancesBaseline) { previous = value; previousAt = at; }
        }

        return produced;
    }

    [Fact]
    public void FirstReading_ProducesNothing_ButBecomesTheBaseline()
    {
        var result = TotalizerDelta.Evaluate(null, null, 12500.20, T0, null);

        Assert.Equal(TotalizerTransition.FirstReading, result.Transition);
        Assert.Null(result.Consumption);
        Assert.True(result.AdvancesBaseline);
    }

    [Fact]
    public void SameValue_ProducesZero()
    {
        var result = TotalizerDelta.Evaluate(12500.20, T0, 12500.20, T0.AddMinutes(5), null);

        Assert.Equal(TotalizerTransition.Unchanged, result.Transition);
        Assert.Equal(0d, result.Consumption);
    }

    [Fact]
    public void Increase_ProducesTheDifference()
    {
        var result = TotalizerDelta.Evaluate(100000.20, T0, 100000.21, T0.AddMinutes(5), null);

        Assert.Equal(TotalizerTransition.Increase, result.Transition);
        Assert.Equal(0.01, result.Consumption!.Value, precision: 9);
    }

    [Fact]
    public void SpecExample_DeltasAndPeriodTotal()
    {
        var produced = Run(null, (0, 1000.00), (5, 1000.10), (10, 1000.35), (15, 1000.50));

        Assert.Equal(3, produced.Count);
        Assert.Equal(0.10, produced[0], precision: 9);
        Assert.Equal(0.25, produced[1], precision: 9);
        Assert.Equal(0.15, produced[2], precision: 9);
        Assert.Equal(0.50, produced.Sum(), precision: 9);
    }

    [Fact]
    public void Decrease_IsAReset_AndNeverProducesNegativeConsumption()
    {
        var result = TotalizerDelta.Evaluate(98000, T0, 12, T0.AddMinutes(5), null);

        Assert.Equal(TotalizerTransition.Decrease, result.Transition);
        Assert.Null(result.Consumption);
        Assert.True(result.AdvancesBaseline);
    }

    [Fact]
    public void AfterReset_CountingResumesFromTheNewMetersReading()
    {
        // Meter replaced between 10:05 and 10:10: the drop is not counted, and the
        // next delta is measured against the new meter, not the old one.
        var produced = Run(null, (0, 98000.0), (5, 98000.5), (10, 3.0), (15, 3.4));

        Assert.Equal(2, produced.Count);
        Assert.Equal(0.5, produced[0], precision: 9);
        Assert.Equal(0.4, produced[1], precision: 9);
        Assert.All(produced, c => Assert.True(c >= 0));
    }

    [Fact]
    public void JumpAboveMaxDelta_IsNotCounted_AndStartsANewPeriod()
    {
        // A replacement meter that starts higher than the old one read.
        var produced = Run(maxDelta: 50, (0, 100.0), (5, 100.2), (10, 9000.0), (15, 9000.3));

        Assert.Equal(new[] { 0.2, 0.3 }, produced.Select(c => Math.Round(c, 9)));
    }

    [Fact]
    public void DuplicateOrOlderReading_IsIgnored_WithoutMovingTheBaseline()
    {
        var sameTime = TotalizerDelta.Evaluate(500, T0.AddMinutes(5), 501, T0.AddMinutes(5), null);
        var older = TotalizerDelta.Evaluate(500, T0.AddMinutes(5), 499, T0, null);

        Assert.Equal(TotalizerTransition.NotNewer, sameTime.Transition);
        Assert.False(sameTime.AdvancesBaseline);
        Assert.Null(sameTime.Consumption);
        Assert.Equal(TotalizerTransition.NotNewer, older.Transition);
        Assert.False(older.AdvancesBaseline);
    }

    [Fact]
    public void DuplicateReadingDelivered_TwiceInSequence_IsCountedOnce()
    {
        var produced = Run(null, (0, 10.0), (5, 10.4), (5, 10.4), (10, 10.9));

        Assert.Equal(0.9, produced.Sum(), precision: 9);
    }

    [Fact]
    public void UnevenIntervals_DeltaDependsOnReadingsNotOnTheGap()
    {
        var produced = Run(null, (0, 50.0), (17, 50.7), (62, 51.0));

        Assert.Equal(1.0, produced.Sum(), precision: 9);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonFiniteReading_IsIgnored(double value)
    {
        var result = TotalizerDelta.Evaluate(500, T0, value, T0.AddMinutes(5), null);

        Assert.Equal(TotalizerTransition.Invalid, result.Transition);
        Assert.False(result.AdvancesBaseline);
    }
}
