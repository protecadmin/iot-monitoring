using Bms.Application.Abstractions.Persistence;
using Bms.Application.Ingestion;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Bms.UnitTests.Ingestion;

/// <summary>
/// Retention exists so RawDeviceMessages cannot fill the disk, because the
/// acknowledgement path writes to that table before anything else. What it
/// must never do is buy that ceiling by discarding a reading that has not
/// reached the time-series store yet.
/// </summary>
public sealed class RawMessageRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static (RawMessageRetentionService Service, Mock<IRawMessageRepository> Repo) Build(
        Action<RawMessageRetentionOptions>? configure = null)
    {
        var settings = new RawMessageRetentionOptions { RetentionDays = 30, BatchSize = 100, MaxBatchesPerPass = 5 };
        configure?.Invoke(settings);

        var repo = new Mock<IRawMessageRepository>(MockBehavior.Strict);
        repo.Setup(r => r.CountExpiredAwaitingTimeSeriesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var clock = new FakeTimeProvider(Now);
        var service = new RawMessageRetentionService(
            repo.Object, Options.Create(settings), clock, NullLogger<RawMessageRetentionService>.Instance);
        return (service, repo);
    }

    [Fact]
    public async Task Deletes_only_messages_older_than_the_retention_window()
    {
        var (service, repo) = Build(o => o.RetentionDays = 30);
        DateTimeOffset? seen = null;
        repo.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<DateTimeOffset, int, CancellationToken>((cutoff, _, _) => seen = cutoff)
            .ReturnsAsync(0);

        await service.RunPassAsync(CancellationToken.None);

        Assert.Equal(Now.AddDays(-30), seen);
    }

    [Fact]
    public async Task Keeps_batching_while_each_batch_comes_back_full()
    {
        var (service, repo) = Build(o => { o.BatchSize = 100; o.MaxBatchesPerPass = 5; });
        var calls = 0;
        repo.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTimeOffset>(), 100, It.IsAny<CancellationToken>()))
            // Three full batches, then a short one: the backlog is drained.
            .ReturnsAsync(() => ++calls <= 3 ? 100 : 12);

        var result = await service.RunPassAsync(CancellationToken.None);

        Assert.Equal(312, result.Deleted);
        Assert.False(result.MoreToDelete);
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task Stops_at_the_batch_ceiling_and_reports_there_is_more()
    {
        var (service, repo) = Build(o => { o.BatchSize = 100; o.MaxBatchesPerPass = 3; });
        repo.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTimeOffset>(), 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        var result = await service.RunPassAsync(CancellationToken.None);

        // One pass must not run unbounded: it stops, says so, and the worker
        // comes straight back rather than holding the database all day.
        Assert.Equal(300, result.Deleted);
        Assert.True(result.MoreToDelete);
        repo.Verify(r => r.DeleteExpiredAsync(It.IsAny<DateTimeOffset>(), 100, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Reports_expired_rows_it_had_to_leave_behind()
    {
        var (service, repo) = Build();
        repo.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        repo.Setup(r => r.CountExpiredAwaitingTimeSeriesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(41);

        var result = await service.RunPassAsync(CancellationToken.None);

        // Old rows whose reading never reached InfluxDB are deliberately kept
        // — the backfill still needs them — so the operator has to be told,
        // otherwise the ceiling silently stops holding.
        Assert.Equal(41, result.AwaitingTimeSeries);
        Assert.Equal(0, result.Deleted);
    }

    [Fact]
    public async Task A_retention_window_of_zero_still_keeps_a_day()
    {
        var (service, repo) = Build(o => o.RetentionDays = 0);
        DateTimeOffset? seen = null;
        repo.Setup(r => r.DeleteExpiredAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<DateTimeOffset, int, CancellationToken>((cutoff, _, _) => seen = cutoff)
            .ReturnsAsync(0);

        await service.RunPassAsync(CancellationToken.None);

        // A misconfigured 0 must not mean "delete everything that exists".
        Assert.Equal(Now.AddDays(-1), seen);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
