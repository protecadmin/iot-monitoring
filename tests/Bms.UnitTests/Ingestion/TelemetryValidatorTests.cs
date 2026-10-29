using Bms.Application.Ingestion;
using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;
using Xunit;

namespace Bms.UnitTests.Ingestion;

public class TelemetryValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly Device _device = new() { Status = DeviceStatus.Online };

    private static readonly IReadOnlyList<DeviceTypeParameter> Parameters =
    [
        new() { Key = "temperature", Label = "Temperature", MinValue = -50, MaxValue = 150 },
        new() { Key = "humidity", Label = "Humidity", MinValue = 0, MaxValue = 100 }
    ];

    private NormalizedTelemetry Telemetry(DateTimeOffset timestamp, double? temperature = 25, double? humidity = 50)
    {
        var fields = new Dictionary<string, double>();
        if (temperature is { } t) fields["temperature"] = t;
        if (humidity is { } h) fields["humidity"] = h;

        return new NormalizedTelemetry
        {
            TenantId = Guid.NewGuid(),
            SiteId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            Timestamp = timestamp,
            IngestedAt = Now,
            Fields = fields
        };
    }

    [Fact]
    public void Validate_NormalReading_ReturnsNull()
    {
        var error = TelemetryValidator.Validate(_device, Telemetry(Now), Parameters, Now);
        Assert.Null(error);
    }

    [Fact]
    public void Validate_FutureTimestampBeyondTolerance_Rejected()
    {
        var error = TelemetryValidator.Validate(_device, Telemetry(Now.AddHours(1)), Parameters, Now);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_StaleTimestamp_Rejected()
    {
        var error = TelemetryValidator.Validate(_device, Telemetry(Now.AddDays(-30)), Parameters, Now);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(-100)]
    [InlineData(500)]
    public void Validate_TemperatureOutOfRange_Rejected(double badTemperature)
    {
        var error = TelemetryValidator.Validate(_device, Telemetry(Now, temperature: badTemperature), Parameters, Now);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_NoRecognizedFields_Rejected()
    {
        var telemetry = Telemetry(Now, temperature: null, humidity: null);
        var error = TelemetryValidator.Validate(_device, telemetry, Parameters, Now);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_DecommissionedDevice_AlwaysRejected()
    {
        var device = new Device { Status = DeviceStatus.Decommissioned };
        var error = TelemetryValidator.Validate(device, Telemetry(Now), Parameters, Now);
        Assert.NotNull(error);
    }
}
