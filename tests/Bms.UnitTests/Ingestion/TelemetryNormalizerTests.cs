using Bms.Application.Abstractions.Persistence;
using Bms.Application.Ingestion;
using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;
using Bms.Protocols.Decoders;
using Moq;
using Xunit;

namespace Bms.UnitTests.Ingestion;

/// <summary>
/// Our gateway sends BMS parameter keys, so a reading is stored under the
/// names it arrives with — filtered only by the device type's parameters.
/// </summary>
public class TelemetryNormalizerTests
{
    private readonly Device _device = new()
    {
        Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), SiteId = Guid.NewGuid(), DeviceTypeId = Guid.NewGuid(),
        PayloadFormat = PayloadFormat.Json
    };

    private TelemetryNormalizer Normalizer(params DeviceTypeParameter[] parameters)
    {
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(d => d.GetByIdAsync(_device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_device);
        var deviceTypes = new Mock<IDeviceTypeRepository>();
        deviceTypes.Setup(t => t.GetByIdAsync(_device.DeviceTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceType { Id = _device.DeviceTypeId, Parameters = parameters.ToList() });
        return new TelemetryNormalizer(devices.Object, deviceTypes.Object, [new JsonPayloadDecoder()]);
    }

    private RawDeviceMessage Message(string json) => new()
    {
        DeviceId = _device.Id, TenantId = _device.TenantId, SiteId = _device.SiteId,
        PayloadFormat = PayloadFormat.Json,
        RawPayload = System.Text.Encoding.UTF8.GetBytes(json),
        ReceivedAt = DateTimeOffset.UtcNow
    };

    private static DeviceTypeParameter P(string key, ParameterKind kind = ParameterKind.Instantaneous) => new() { Key = key, Label = key, Kind = kind };

    [Fact]
    public async Task GatewayReading_IsKeptUnderItsOwnParameterKeys()
    {
        var result = await Normalizer(P("current"), P("voltage"), P("running", ParameterKind.Digital))
            .NormalizeAsync(Message("""{"current": 12.4, "voltage": "231.5", "running": true, "seq": 7}"""), CancellationToken.None);

        var fields = result.Telemetry!.Fields;
        Assert.Equal(12.4, fields["current"]);
        Assert.Equal(231.5, fields["voltage"]);
        Assert.Equal(1, fields["running"]);
        Assert.Equal(7, result.Telemetry.SequenceNumber);
    }

    [Fact]
    public async Task KeyNotDefinedOnTheDeviceType_IsDropped()
    {
        var result = await Normalizer(P("current"))
            .NormalizeAsync(Message("""{"current": 12.4, "temp": 25, "label": "meter-1"}"""), CancellationToken.None);

        Assert.Equal(["current"], result.Telemetry!.Fields.Keys);
    }

    [Fact]
    public async Task KeysMatchParametersCaseInsensitively()
    {
        var result = await Normalizer(P("powerFactor"))
            .NormalizeAsync(Message("""{"PowerFactor": 0.95}"""), CancellationToken.None);

        Assert.Equal(0.95, result.Telemetry!.Fields["powerFactor"]);
    }

    [Fact]
    public async Task ValueConversion_CorrectsTheValueUnderTheSameKey()
    {
        _device.ValueConversions =
        [
            new DeviceValueConversion { ParameterKey = "xDeg", Scale = 0.001 },
            new DeviceValueConversion { ParameterKey = "temperature", Offset = 32, Scale = 5.0 / 9 }
        ];

        var result = await Normalizer(P("xDeg"), P("temperature"), P("batteryLevel"))
            .NormalizeAsync(Message("""{"xDeg": 12500, "Temperature": 212, "batteryLevel": 8}"""), CancellationToken.None);

        var fields = result.Telemetry!.Fields;
        Assert.Equal(12.5, fields["xDeg"], 9);
        Assert.Equal(100, fields["temperature"], 9);
        Assert.Equal(8, fields["batteryLevel"]);
    }

    [Fact]
    public async Task ValueConversion_SkipsAFieldTheReadingDoesNotCarry()
    {
        _device.ValueConversions = [new DeviceValueConversion { ParameterKey = "xDeg", Scale = 0.001 }];

        var result = await Normalizer(P("xDeg"), P("batteryLevel"))
            .NormalizeAsync(Message("""{"batteryLevel": 8}"""), CancellationToken.None);

        Assert.Equal(["batteryLevel"], result.Telemetry!.Fields.Keys);
    }

    [Fact]
    public async Task ValueConversion_NeverAppliesToAConnectorDevice()
    {
        // A connector device is converted by its connector's mapping — doing it here too would convert twice.
        _device.Connection = DeviceConnection.Connector;
        _device.ValueConversions = [new DeviceValueConversion { ParameterKey = "xDeg", Scale = 0.001 }];

        var result = await Normalizer(P("xDeg"))
            .NormalizeAsync(Message("""{"xDeg": 12.5}"""), CancellationToken.None);

        Assert.Equal(12.5, result.Telemetry!.Fields["xDeg"]);
    }
}
