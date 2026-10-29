using System.Net.Sockets;
using Bms.Application.Abstractions.Alerting;
using Bms.Application.Abstractions.Calculations;
using Bms.Application.Abstractions.DeviceManagement;
using Bms.Application.Abstractions.Notifications;
using Bms.Application.Abstractions.Payloads;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Realtime;
using Bms.Application.Abstractions.Telemetry;
using Bms.Application.Ingestion;
using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;
using Bms.Infrastructure.Persistence;
using Bms.Protocols.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using Xunit;

namespace Bms.UnitTests.Ingestion;

/// <summary>
/// The acknowledgement boundary and duplicate identity: a message is
/// released to the broker only once PostgreSQL holds it, transient
/// infrastructure failures are never swallowed, and redeliveries are
/// recognised without discarding legitimate readings.
/// </summary>
public class IngestionReliabilityTests
{
    private readonly Mock<IDeviceRepository> _devices = new();
    private readonly Mock<IDeviceTypeRepository> _deviceTypes = new();
    private readonly Mock<IRawMessageRepository> _rawMessages = new();
    private readonly Mock<IPayloadDecoder> _decoder = new();
    private readonly Mock<ITelemetryStore> _telemetryStore = new();
    private readonly Mock<IAlertEvaluator> _alertEvaluator = new();
    private readonly List<string> _events = [];

    private readonly Device _device = new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        SiteId = Guid.NewGuid(),
        DeviceTypeId = Guid.NewGuid(),
        PayloadFormat = PayloadFormat.Json,
        Status = DeviceStatus.Online
    };

    public IngestionReliabilityTests()
    {
        _devices.Setup(d => d.GetByIdAsync(_device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_device);
        _decoder.Setup(d => d.Format).Returns(PayloadFormat.Json);
        _deviceTypes.Setup(t => t.GetByIdAsync(_device.DeviceTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceType { Id = _device.DeviceTypeId, Parameters = [new DeviceTypeParameter { Key = "temperature", Label = "Temperature" }] });
        _alertEvaluator.Setup(a => a.EvaluateAsync(It.IsAny<Device>(), It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Alert>());
        _rawMessages.Setup(r => r.TryAddPersistedAsync(It.IsAny<RawDeviceMessage>(), It.IsAny<CancellationToken>()))
            .Callback(() => _events.Add("persisted"))
            .ReturnsAsync(true);
        _rawMessages.Setup(r => r.AddAsync(It.IsAny<RawDeviceMessage>(), It.IsAny<CancellationToken>()))
            .Callback<RawDeviceMessage, CancellationToken>((m, _) => _events.Add($"recorded:{m.ProcessingStatus}"))
            .ReturnsAsync((RawDeviceMessage m, CancellationToken _) => m);
        _telemetryStore.Setup(t => t.WriteAsync(It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()))
            .Callback(() => _events.Add("influx"))
            .Returns(Task.CompletedTask);
    }

    private IngestionPipeline BuildPipeline() => new(
        _devices.Object, _deviceTypes.Object, _rawMessages.Object,
        [_decoder.Object],
        _telemetryStore.Object, _alertEvaluator.Object, new Mock<IDeviceLifecycleService>().Object, new Mock<IRealtimeNotifier>().Object,
        new Mock<IAlertEmailNotifier>().Object, new Mock<IAlertTelegramNotifier>().Object,
        new Mock<ICalculationEngine>().Object,
        new TransientFailureClassifier(),
        Options.Create(new IngestionReliabilityOptions { DuplicateWindowHours = 24 }),
        TimeProvider.System, NullLogger<IngestionPipeline>.Instance);

    private RawDeviceMessage RawMessage(string payload = """{"temp":25}""") => new()
    {
        DeviceId = _device.Id,
        TenantId = _device.TenantId,
        SiteId = _device.SiteId,
        PayloadFormat = PayloadFormat.Json,
        RawPayload = System.Text.Encoding.UTF8.GetBytes(payload),
        ReceivedAt = DateTimeOffset.UtcNow
    };

    private void Decodes(long? sequence, DateTimeOffset? deviceTimestamp) =>
        _decoder.Setup(d => d.Decode(It.IsAny<ReadOnlyMemory<byte>>()))
            .Returns(new DecodedPayload { Fields = new Dictionary<string, object?> { ["temperature"] = 25.0 }, SequenceNumber = sequence, DeviceTimestamp = deviceTimestamp });

    private static NormalizedTelemetry Reading(long? sequence, DateTimeOffset timestamp) => new()
    {
        TenantId = Guid.Empty, SiteId = Guid.Empty, DeviceId = Guid.Empty,
        SequenceNumber = sequence, Timestamp = timestamp, IngestedAt = timestamp, Fields = new Dictionary<string, double>()
    };

    private Func<CancellationToken, Task> Ack() => _ => { _events.Add("ack"); return Task.CompletedTask; };

    [Fact]
    public async Task ValidMessage_IsAcknowledgedAfterPostgresAndBeforeInflux()
    {
        Decodes(1, DateTimeOffset.UtcNow);

        await BuildPipeline().ProcessAsync(RawMessage(), Ack(), CancellationToken.None);

        Assert.Equal(["persisted", "ack", "influx"], _events);
        _rawMessages.Verify(r => r.MarkTimeSeriesWrittenAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PostgresUnreachable_Throws_AndNeverAcknowledges()
    {
        _devices.Setup(d => d.GetByIdAsync(_device.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NpgsqlException("Failed to connect", new SocketException((int)SocketError.ConnectionRefused)));

        await Assert.ThrowsAnyAsync<Exception>(() => BuildPipeline().ProcessAsync(RawMessage(), Ack(), CancellationToken.None));

        Assert.DoesNotContain("ack", _events);
        _rawMessages.Verify(r => r.AddAsync(It.IsAny<RawDeviceMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PostgresDropsDuringInsert_Throws_AndNeverAcknowledges()
    {
        Decodes(1, DateTimeOffset.UtcNow);
        _rawMessages.Setup(r => r.TryAddPersistedAsync(It.IsAny<RawDeviceMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("save failed", new NpgsqlException("Exception while reading from stream", new IOException("reset"))));

        await Assert.ThrowsAnyAsync<Exception>(() => BuildPipeline().ProcessAsync(RawMessage(), Ack(), CancellationToken.None));

        Assert.DoesNotContain("ack", _events);
        _telemetryStore.Verify(t => t.WriteAsync(It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MalformedPayload_IsRecordedAsRejected_AndAcknowledged()
    {
        _decoder.Setup(d => d.Decode(It.IsAny<ReadOnlyMemory<byte>>())).Throws(new FormatException("bad json"));

        await BuildPipeline().ProcessAsync(RawMessage("{not json"), Ack(), CancellationToken.None);

        Assert.Equal(["recorded:Rejected", "ack"], _events);
    }

    [Fact]
    public async Task UnexpectedNonTransientBug_IsRecordedAsRejected_AndAcknowledged_NotRetriedForever()
    {
        _deviceTypes.Setup(t => t.GetByIdAsync(_device.DeviceTypeId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("corrupt device type"));
        Decodes(1, DateTimeOffset.UtcNow);

        await BuildPipeline().ProcessAsync(RawMessage(), Ack(), CancellationToken.None);

        Assert.Equal(["recorded:Rejected", "ack"], _events);
    }

    [Fact]
    public async Task InfluxDown_RawStillPersistedAndAcknowledged_LeftPendingForBackfill()
    {
        Decodes(1, DateTimeOffset.UtcNow);
        _telemetryStore.Setup(t => t.WriteAsync(It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        await BuildPipeline().ProcessAsync(RawMessage(), Ack(), CancellationToken.None);

        Assert.Equal(["persisted", "ack"], _events);
        _rawMessages.Verify(r => r.MarkTimeSeriesWrittenAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UniqueIndexRace_IsRecordedAsDuplicate_AcknowledgedAndNotWrittenTwice()
    {
        Decodes(1, DateTimeOffset.UtcNow);
        _rawMessages.Setup(r => r.TryAddPersistedAsync(It.IsAny<RawDeviceMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var message = RawMessage();

        await BuildPipeline().ProcessAsync(message, Ack(), CancellationToken.None);

        Assert.Equal(MessageProcessingStatus.Rejected, message.ProcessingStatus);
        Assert.Contains("Duplicate", message.ProcessingError);
        Assert.Equal(["recorded:Rejected", "ack"], _events);
        _telemetryStore.Verify(t => t.WriteAsync(It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SameReadingTwice_WithSequenceAndTimestamp_UsesStableKey()
    {
        var ts = DateTimeOffset.UtcNow.AddSeconds(-30);
        Decodes(42, ts);
        var first = RawMessage();
        var second = RawMessage();

        await BuildPipeline().ProcessAsync(first, CancellationToken.None);
        await BuildPipeline().ProcessAsync(second, CancellationToken.None);

        Assert.NotNull(first.DedupKey);
        Assert.Equal(first.DedupKey, second.DedupKey);
        _rawMessages.Verify(r => r.ExistsByDedupKeyAsync(_device.Id, first.DedupKey!, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SequenceReset_WithNewTimestamp_IsNotTreatedAsDuplicate()
    {
        // A device rebooted and restarted its counter at 1: same sequence
        // number as an old reading, but a different device timestamp.
        var oldKey = IngestionPipeline.BuildDedupKey(Reading(1, DateTimeOffset.UtcNow.AddHours(-3)), true);
        _rawMessages.Setup(r => r.ExistsByDedupKeyAsync(_device.Id, oldKey!, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Decodes(1, DateTimeOffset.UtcNow);
        var message = RawMessage();

        await BuildPipeline().ProcessAsync(message, CancellationToken.None);

        Assert.Equal(MessageProcessingStatus.Persisted, message.ProcessingStatus);
        Assert.NotEqual(oldKey, message.DedupKey);
    }

    [Fact]
    public async Task SequenceWithoutTimestamp_IsCheckedOnlyWithinWindow_AndAgainstIdenticalPayload()
    {
        Decodes(65535, null);
        var message = RawMessage("""{"seq":65535,"temp":25}""");

        await BuildPipeline().ProcessAsync(message, CancellationToken.None);

        Assert.Null(message.DedupKey);
        Assert.Equal(MessageProcessingStatus.Persisted, message.ProcessingStatus);
        _rawMessages.Verify(r => r.ExistsRecentAsync(
            _device.Id, 65535, message.RawPayload,
            It.Is<DateTimeOffset>(since => since > DateTimeOffset.UtcNow.AddHours(-24.1) && since < DateTimeOffset.UtcNow.AddHours(-23.9)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoSequenceAndNoTimestamp_IsNeverDiscardedAsDuplicate()
    {
        Decodes(null, null);
        var message = RawMessage();

        await BuildPipeline().ProcessAsync(message, CancellationToken.None);

        Assert.Equal(MessageProcessingStatus.Persisted, message.ProcessingStatus);
        _rawMessages.Verify(r => r.ExistsByDedupKeyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _rawMessages.Verify(r => r.ExistsRecentAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<byte[]>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void DedupKey_DependsOnlyOnDeviceSuppliedValues()
    {
        var ts = DateTimeOffset.Parse("2026-10-04T10:00:00.123Z");
        Assert.Equal("s5@1791108000123", IngestionPipeline.BuildDedupKey(Reading(5, ts), true));
        Assert.Equal("@1791108000123", IngestionPipeline.BuildDedupKey(Reading(null, ts), true));
        Assert.Null(IngestionPipeline.BuildDedupKey(Reading(5, ts), false));
    }
}

public class TransientFailureClassifierTests
{
    private readonly TransientFailureClassifier _classifier = new();

    [Fact]
    public void DatabaseConnectivityFailures_AreTransient()
    {
        Assert.True(_classifier.IsTransient(new NpgsqlException("connect failed", new SocketException((int)SocketError.ConnectionRefused))));
        Assert.True(_classifier.IsTransient(new DbUpdateException("x", new NpgsqlException("read failed", new IOException()))));
        Assert.True(_classifier.IsTransient(new InvalidOperationException("wrapped", new TimeoutException())));
        Assert.True(_classifier.IsTransient(new PostgresException("shutting down", "FATAL", "FATAL", "57P01")));
        Assert.True(_classifier.IsTransient(new HttpRequestException("influx down")));
        Assert.True(_classifier.IsTransient(new TaskCanceledException()));
    }

    [Fact]
    public void DataProblems_AreNotTransient()
    {
        Assert.False(_classifier.IsTransient(new PostgresException("duplicate key", "ERROR", "ERROR", "23505")));
        Assert.False(_classifier.IsTransient(new DbUpdateException("x", new PostgresException("value too long", "ERROR", "ERROR", "22001"))));
        Assert.False(_classifier.IsTransient(new FormatException("bad payload")));
        Assert.False(_classifier.IsTransient(new InvalidOperationException("bug")));
    }
}

public class TimeSeriesBackfillServiceTests
{
    private readonly Mock<IRawMessageRepository> _rawMessages = new();
    private readonly Mock<IDeviceRepository> _devices = new();
    private readonly Mock<ITelemetryStore> _store = new();
    private readonly Mock<IPayloadDecoder> _decoder = new();
    private readonly Device _device = new() { Id = Guid.NewGuid(), DeviceTypeId = Guid.NewGuid(), PayloadFormat = PayloadFormat.Json };

    private TimeSeriesBackfillService Build(params RawDeviceMessage[] pending)
    {
        _rawMessages.Setup(r => r.ListPendingTimeSeriesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);
        _devices.Setup(d => d.GetByIdAsync(_device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_device);
        _decoder.Setup(d => d.Format).Returns(PayloadFormat.Json);
        _decoder.Setup(d => d.Decode(It.IsAny<ReadOnlyMemory<byte>>())).Returns(new DecodedPayload { Fields = new Dictionary<string, object?> { ["temperature"] = 21.0 } });
        var deviceTypes = new Mock<IDeviceTypeRepository>();
        deviceTypes.Setup(t => t.GetByIdAsync(_device.DeviceTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceType { Id = _device.DeviceTypeId, Parameters = [new DeviceTypeParameter { Key = "temperature", Label = "T" }] });

        var normalizer = new TelemetryNormalizer(_devices.Object, deviceTypes.Object, [_decoder.Object]);
        return new TimeSeriesBackfillService(_rawMessages.Object, normalizer, _store.Object, new TransientFailureClassifier(),
            Options.Create(new TimeSeriesBackfillOptions()), TimeProvider.System, NullLogger<TimeSeriesBackfillService>.Instance);
    }

    private RawDeviceMessage Pending(DateTimeOffset deviceTimestamp) => new()
    {
        DeviceId = _device.Id,
        PayloadFormat = PayloadFormat.Json,
        RawPayload = """{"temp":21}"""u8.ToArray(),
        ReceivedAt = deviceTimestamp.AddSeconds(1),
        DeviceTimestamp = deviceTimestamp,
        ProcessingStatus = MessageProcessingStatus.Persisted
    };

    [Fact]
    public async Task WritesPendingReadingsAtTheirOriginalTimestamp_AndMarksThemWritten()
    {
        var ts = DateTimeOffset.UtcNow.AddMinutes(-20);
        var message = Pending(ts);
        NormalizedTelemetry? written = null;
        _store.Setup(s => s.WriteAsync(It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()))
            .Callback<NormalizedTelemetry, CancellationToken>((t, _) => written = t).Returns(Task.CompletedTask);

        var result = await Build(message).RunPassAsync(CancellationToken.None);

        Assert.Equal(1, result.Written);
        Assert.Equal(ts, written!.Timestamp);
        Assert.Equal(21.0, written.Fields["temperature"]);
        _rawMessages.Verify(r => r.MarkTimeSeriesWrittenAsync(message.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        // Never re-inserts the raw message.
        _rawMessages.Verify(r => r.AddAsync(It.IsAny<RawDeviceMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InfluxStillDown_StopsThePass_AndCountsOneAttempt()
    {
        _store.Setup(s => s.WriteAsync(It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));
        var first = Pending(DateTimeOffset.UtcNow.AddMinutes(-10));
        var second = Pending(DateTimeOffset.UtcNow.AddMinutes(-9));

        var result = await Build(first, second).RunPassAsync(CancellationToken.None);

        Assert.True(result.StoreUnavailable);
        _store.Verify(s => s.WriteAsync(It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()), Times.Once);
        _rawMessages.Verify(r => r.RecordTimeSeriesAttemptFailedAsync(first.Id, It.IsAny<CancellationToken>()), Times.Once);
        _rawMessages.Verify(r => r.MarkTimeSeriesWrittenAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public class IngestionHealthTrackerTests
{
    private static IngestionHealthTracker Tracker(bool brokerConnected = true) =>
        new(() => brokerConnected, Options.Create(new IngestionHealthOptions { StaleAfterSeconds = 60, RecoveryGraceSeconds = 180 }));

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void HealthyAndPastGrace_DeviceSilenceIsTrusted()
    {
        var tracker = Tracker();
        tracker.Update(new IngestionStatusMessage(IngestionStatusMessage.Online, Now.AddSeconds(-10), null, Now.AddMinutes(-30)));
        Assert.Null(tracker.DeviceSilenceUntrustedReason(Now));
    }

    [Fact]
    public void IngestionOfflineDegradedStaleOrDraining_DeviceSilenceIsNotTrusted()
    {
        var tracker = Tracker();
        Assert.NotNull(tracker.DeviceSilenceUntrustedReason(Now)); // nothing heard yet

        tracker.Update(new IngestionStatusMessage(IngestionStatusMessage.Offline, Now));
        Assert.Contains("offline", tracker.DeviceSilenceUntrustedReason(Now));

        tracker.Update(new IngestionStatusMessage(IngestionStatusMessage.Degraded, Now, "PostgreSQL down"));
        Assert.Contains("degraded", tracker.DeviceSilenceUntrustedReason(Now));

        tracker.Update(new IngestionStatusMessage(IngestionStatusMessage.Online, Now.AddMinutes(-5), null, Now.AddHours(-1)));
        Assert.Contains("not running", tracker.DeviceSilenceUntrustedReason(Now));

        tracker.Update(new IngestionStatusMessage(IngestionStatusMessage.Online, Now, null, Now.AddSeconds(-30)));
        Assert.Contains("draining", tracker.DeviceSilenceUntrustedReason(Now));

        Assert.NotNull(Tracker(brokerConnected: false).DeviceSilenceUntrustedReason(Now));
    }

    [Fact]
    public void LegacyBareOfflineWill_IsUnderstood()
    {
        Assert.Equal(IngestionStatusMessage.Offline, IngestionStatusMessage.TryParse("offline"u8)!.Status);
        var roundTrip = IngestionStatusMessage.TryParse(new IngestionStatusMessage(IngestionStatusMessage.Online, Now, null, Now).ToUtf8Bytes());
        Assert.Equal(IngestionStatusMessage.Online, roundTrip!.Status);
    }
}
