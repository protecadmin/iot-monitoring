using Bms.Connectors.Core;
using Bms.Domain.Connectors;
using Xunit;

namespace Bms.UnitTests.Connectors;

public class ConnectorValidatorTests
{
    private static Connector Valid() => new()
    {
        Name = "FG-WM400", Port = 12000, Format = ConnectorFormat.Csv, DeviceCodePrefix = "fgwm400-",
        Mappings = [new() { Source = "1", Target = ConnectorTarget.SensorId }, new() { Source = "2", ParameterKey = "xDeg" }]
    };

    [Fact]
    public void ValidConnector_HasNoErrors() => Assert.Empty(ConnectorValidator.Validate(Valid(), 12000, 12099));

    [Fact]
    public void NoMappingYet_IsAValidDraft()
    {
        var c = Valid();
        c.Mappings.Clear();
        Assert.Empty(ConnectorValidator.Validate(c, 12000, 12099));
    }

    [Fact]
    public void PortOutsideTheOpenRange_IsRefused()
    {
        var c = Valid();
        c.Port = 8080;
        Assert.Contains(ConnectorValidator.Validate(c, 12000, 12099), e => e.Contains("Port"));
    }

    [Fact]
    public void NoSensorId_IsRefused()
    {
        var c = Valid();
        c.Mappings.RemoveAt(0);
        Assert.Contains(ConnectorValidator.Validate(c, 12000, 12099), e => e.Contains("Sensor ID"));
    }

    [Fact]
    public void BinaryOnNewLineFraming_IsRefused()
    {
        var c = Valid();
        c.Format = ConnectorFormat.Binary;
        c.Mappings = [new() { Source = "0", Target = ConnectorTarget.SensorId }, new() { Source = "1", ParameterKey = "x", ByteLength = 2 }];
        Assert.Contains(ConnectorValidator.Validate(c, 12000, 12099), e => e.Contains("Fixed length"));
    }

    [Fact]
    public void SameParameterTwice_IsRefused()
    {
        var c = Valid();
        c.Mappings.Add(new() { Source = "3", ParameterKey = "XDEG" });
        Assert.Contains(ConnectorValidator.Validate(c, 12000, 12099), e => e.Contains("more than once"));
    }
}
