using Bms.Application.Abstractions.Alerting;
using Bms.Application.Abstractions.Calculations;
using Bms.Application.Abstractions.DeviceManagement;
using Bms.Application.Abstractions.Payloads;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Realtime;
using Bms.Application.Abstractions.Telemetry;
using Bms.Application.Ingestion;
using Bms.Application.Abstractions.Notifications;
using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;
using Bms.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Bms.UnitTests.Ingestion;

public class IngestionPipelineTests
{
    private readonly Mock<IDeviceRepository> _devices = new();
    private readonly Mock<IDeviceTypeRepository> _deviceTypes = new();
    private readonly Mock<IRawMessageRepository> _rawMessages = new();
    private readonly Mock<IPayloadDecoder> _decoder = new();
    private readonly Mock<ITelemetryStore> _telemetryStore = new();
    private readonly Mock<IAlertEvaluator> _alertEvaluator = new();
    private readonly Mock<IDeviceLifecycleService> _deviceLifecycle = new();
    private readonly Mock<IRealtimeNotifier> _notifier = new();
    private readonly Mock<IAlertTelegramNotifier> _telegramNotifier = new();
    private readonly Mock<ICalculationEngine> _calculations = new();

    private readonly Device _device = new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        SiteId = Guid.NewGuid(),
        DeviceTypeId = Guid.NewGuid(),
        PayloadFormat = PayloadFormat.Json,
        Status = DeviceStatus.Online
    };

    private IngestionPipeline BuildPipeline()
    {
        _decoder.Setup(d => d.Format).Returns(PayloadFormat.Json);
        _alertEvaluator.Setup(a => a.EvaluateAsync(It.IsAny<Device>(), It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Alert>());
        _rawMessages.Setup(r => r.TryAddPersistedAsync(It.IsAny<RawDeviceMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // "temperature" must be an allowed parameter on the device's type,
        // or the pipeline's allow-list filter drops it before it ever
        // reaches the telemetry store — see IngestionPipeline.
        _deviceTypes.Setup(t => t.GetByIdAsync(_device.DeviceTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceType
            {
                Id = _device.DeviceTypeId,
                Parameters = [new DeviceTypeParameter { Key = "temperature", Label = "Temperature" }]
            });

        return new IngestionPipeline(
            _devices.Object, _deviceTypes.Object, _rawMessages.Object,
            [_decoder.Object],
            _telemetryStore.Object, _alertEvaluator.Object, _deviceLifecycle.Object, _notifier.Object,
            // Alert notifications are isolated side effects the pipeline tests
            // don't assert on; loose mocks satisfy the constructor.
            new Mock<IAlertEmailNotifier>().Object, _telegramNotifier.Object,
            _calculations.Object,
            new TransientFailureClassifier(),
            Options.Create(new IngestionReliabilityOptions()),
            TimeProvider.System, NullLogger<IngestionPipeline>.Instance);
    }

    private RawDeviceMessage BuildRawMessage() => new()
    {
        DeviceId = _device.Id,
        TenantId = _device.TenantId,
        SiteId = _device.SiteId,
        PayloadFormat = PayloadFormat.Json,
        RawPayload = "{}"u8.ToArray(),
        ReceivedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task ProcessAsync_UnknownDevice_RejectsWithoutThrowing()
    {
        _devices.Setup(d => d.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Device?)null);
        var pipeline = BuildPipeline();
        var message = BuildRawMessage();

        await pipeline.ProcessAsync(message, CancellationToken.None);

        Assert.Equal(MessageProcessingStatus.Rejected, message.ProcessingStatus);
        Assert.Contains("Unknown device", message.ProcessingError);
        _rawMessages.Verify(r => r.AddAsync(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_DecoderThrows_RejectsWithoutThrowing()
    {
        _devices.Setup(d => d.GetByIdAsync(_device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_device);
        _decoder.Setup(d => d.Decode(It.IsAny<ReadOnlyMemory<byte>>())).Throws(new FormatException("bad json"));
        var pipeline = BuildPipeline();
        var message = BuildRawMessage();

        await pipeline.ProcessAsync(message, CancellationToken.None);

        Assert.Equal(MessageProcessingStatus.Rejected, message.ProcessingStatus);
        Assert.Contains("Decode failed", message.ProcessingError);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateTelemetry_RejectsAndDoesNotWriteToTelemetryStore()
    {
        _devices.Setup(d => d.GetByIdAsync(_device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_device);
        _decoder.Setup(d => d.Decode(It.IsAny<ReadOnlyMemory<byte>>()))
            .Returns(new DecodedPayload
            {
                Fields = new Dictionary<string, object?> { ["temperature"] = 25.0 },
                SequenceNumber = 7,
                DeviceTimestamp = DateTimeOffset.UtcNow.AddSeconds(-5)
            });
        _rawMessages.Setup(r => r.ExistsByDedupKeyAsync(_device.Id, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var pipeline = BuildPipeline();
        var message = BuildRawMessage();

        await pipeline.ProcessAsync(message, CancellationToken.None);

        Assert.Equal(MessageProcessingStatus.Rejected, message.ProcessingStatus);
        Assert.Contains("Duplicate", message.ProcessingError);
        _telemetryStore.Verify(t => t.WriteAsync(It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ValidMessage_PersistsWritesTelemetryAndNotifies()
    {
        _devices.Setup(d => d.GetByIdAsync(_device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_device);
        _decoder.Setup(d => d.Decode(It.IsAny<ReadOnlyMemory<byte>>()))
            .Returns(new DecodedPayload { Fields = new Dictionary<string, object?> { ["temperature"] = 25.0 } });

        var pipeline = BuildPipeline();
        var message = BuildRawMessage();

        await pipeline.ProcessAsync(message, CancellationToken.None);

        Assert.Equal(MessageProcessingStatus.Persisted, message.ProcessingStatus);
        _telemetryStore.Verify(t => t.WriteAsync(It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()), Times.Once);
        _deviceLifecycle.Verify(l => l.RecordTelemetryAsync(_device, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.TelemetryUpdatedAsync(It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_MappedFieldNotInDeviceTypeParameters_IsDroppedBeforeStorage()
    {
        // The exact behavior a user asked about: an Energy Meter sending
        // current/voltage alongside energy should only keep whatever its
        // Device Type actually defines as a parameter — "current" here is
        // successfully mapped but isn't in the device's type (only
        // "temperature" is, per BuildPipeline), so it must never reach
        // InfluxDB even though the mapper produced it.
        _devices.Setup(d => d.GetByIdAsync(_device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_device);
        _decoder.Setup(d => d.Decode(It.IsAny<ReadOnlyMemory<byte>>()))
            .Returns(new DecodedPayload { Fields = new Dictionary<string, object?> { ["temperature"] = 25.0, ["current"] = 10.0 } });

        NormalizedTelemetry? written = null;
        _telemetryStore.Setup(t => t.WriteAsync(It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()))
            .Callback<NormalizedTelemetry, CancellationToken>((t, _) => written = t)
            .Returns(Task.CompletedTask);

        var pipeline = BuildPipeline();
        var message = BuildRawMessage();

        await pipeline.ProcessAsync(message, CancellationToken.None);

        Assert.Equal(MessageProcessingStatus.Persisted, message.ProcessingStatus);
        Assert.NotNull(written);
        Assert.True(written!.Fields.ContainsKey("temperature"));
        Assert.False(written.Fields.ContainsKey("current"));
    }

    [Fact]
    public async Task ProcessAsync_TelemetryStoreThrows_StillRecordsDeviceLifecycleAndDoesNotDoubleInsertRawMessage()
    {
        // Regression test for a real bug found during manual end-to-end
        // testing: an InfluxDB outage after the raw message was already
        // marked Persisted used to crash with a duplicate-key error
        // because the failure handler tried to insert the same row again.
        _devices.Setup(d => d.GetByIdAsync(_device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_device);
        _decoder.Setup(d => d.Decode(It.IsAny<ReadOnlyMemory<byte>>()))
            .Returns(new DecodedPayload { Fields = new Dictionary<string, object?> { ["temperature"] = 25.0 } });
        _telemetryStore.Setup(t => t.WriteAsync(It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("InfluxDB unreachable"));

        var pipeline = BuildPipeline();
        var message = BuildRawMessage();

        await pipeline.ProcessAsync(message, CancellationToken.None);

        Assert.Equal(MessageProcessingStatus.Persisted, message.ProcessingStatus);
        _rawMessages.Verify(r => r.TryAddPersistedAsync(message, It.IsAny<CancellationToken>()), Times.Once); // exactly once, not twice
        _rawMessages.Verify(r => r.AddAsync(message, It.IsAny<CancellationToken>()), Times.Never);
        // Left pending for the time-series backfill.
        _rawMessages.Verify(r => r.MarkTimeSeriesWrittenAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        _deviceLifecycle.Verify(l => l.RecordTelemetryAsync(_device, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_TelegramUnreachable_TelemetryAndAlertingAreUnaffected()
    {
        // Notification channels are isolated effects: BMS detects conditions
        // whether or not anything downstream is listening. A Telegram outage
        // must not cost a single telemetry point, and must not mark the
        // message rejected.
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            TenantId = _device.TenantId,
            DeviceId = _device.Id,
            Severity = AlertSeverity.Critical,
            Parameter = "temperature",
            Message = "Temperature above threshold"
        };

        _devices.Setup(d => d.GetByIdAsync(_device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_device);
        _decoder.Setup(d => d.Decode(It.IsAny<ReadOnlyMemory<byte>>()))
            .Returns(new DecodedPayload { Fields = new Dictionary<string, object?> { ["temperature"] = 95.0 } });
        _telegramNotifier.Setup(c => c.NotifyAsync(It.IsAny<Alert>(), It.IsAny<Device>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Telegram unreachable"));

        var pipeline = BuildPipeline();
        // Re-arm the evaluator after BuildPipeline's default empty setup.
        _alertEvaluator.Setup(a => a.EvaluateAsync(It.IsAny<Device>(), It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Alert> { alert });

        var message = BuildRawMessage();
        await pipeline.ProcessAsync(message, CancellationToken.None);

        Assert.Equal(MessageProcessingStatus.Persisted, message.ProcessingStatus);
        Assert.Null(message.ProcessingError);
        _telemetryStore.Verify(t => t.WriteAsync(It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()), Times.Once);
        _deviceLifecycle.Verify(l => l.RecordTelemetryAsync(_device, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        // The in-app alert push still happened — one channel failing must not
        // take the others with it.
        _notifier.Verify(n => n.AlertCreatedAsync(alert, It.IsAny<CancellationToken>()), Times.Once);
        _telegramNotifier.Verify(c => c.NotifyAsync(alert, _device, It.IsAny<CancellationToken>()), Times.Once);
    }

    private void CalculationProduces(string outputKey, double value) =>
        _calculations.Setup(c => c.EvaluateAsync(_device, It.IsAny<IReadOnlyList<DeviceTypeParameter>>(), It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CalculationUpdate(_device.TenantId, _device.SiteId, _device.Id, Guid.NewGuid(), "rule", outputKey, outputKey, "kW", value, DateTimeOffset.UtcNow)]);

    [Fact]
    public async Task ProcessAsync_AlertRulesSeeCalculatedValues()
    {
        _devices.Setup(d => d.GetByIdAsync(_device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_device);
        _decoder.Setup(d => d.Decode(It.IsAny<ReadOnlyMemory<byte>>()))
            .Returns(new DecodedPayload { Fields = new Dictionary<string, object?> { ["temperature"] = 25.0 } });
        CalculationProduces("activePower", 52.5);
        var pipeline = BuildPipeline();

        await pipeline.ProcessAsync(BuildRawMessage(), CancellationToken.None);

        // "activePower > 50" can fire: the evaluator gets the calculated value
        // beside the measured ones, while storage only ever gets what was measured.
        _alertEvaluator.Verify(a => a.EvaluateAsync(_device,
            It.Is<NormalizedTelemetry>(t => t.Fields["activePower"] == 52.5 && t.Fields["temperature"] == 25.0), It.IsAny<CancellationToken>()), Times.Once);
        _telemetryStore.Verify(t => t.WriteAsync(It.Is<NormalizedTelemetry>(n => !n.Fields.ContainsKey("activePower")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_MeasuredValueWinsOverACalculatedOneForAlerts()
    {
        _devices.Setup(d => d.GetByIdAsync(_device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_device);
        _decoder.Setup(d => d.Decode(It.IsAny<ReadOnlyMemory<byte>>()))
            .Returns(new DecodedPayload { Fields = new Dictionary<string, object?> { ["temperature"] = 25.0 } });
        CalculationProduces("temperature", 99.0);
        var pipeline = BuildPipeline();

        await pipeline.ProcessAsync(BuildRawMessage(), CancellationToken.None);

        _alertEvaluator.Verify(a => a.EvaluateAsync(_device,
            It.Is<NormalizedTelemetry>(t => t.Fields["temperature"] == 25.0), It.IsAny<CancellationToken>()), Times.Once);
    }
}
