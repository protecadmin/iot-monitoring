using System.Text;
using Bms.Connectors.Core;
using Bms.Domain.Connectors;
using Xunit;

namespace Bms.UnitTests.Connectors;

public class MessageDecoderTests
{
    /// <summary>Pro-Tec's FG-WM400 tilt sensor, text with acceleration (columns 2-9), phone in column 10.</summary>
    private static Connector FgWm400Text() => new()
    {
        Name = "FG-WM400", Port = 12000, Framing = ConnectorFraming.NewLine, Format = ConnectorFormat.Csv,
        CsvOptionalPrefix = true, DeviceCodePrefix = "fgwm400-",
        Mappings =
        [
            new() { Source = "1", Target = ConnectorTarget.SensorId },
            new() { Source = "2", ParameterKey = "xDeg" }, new() { Source = "3", ParameterKey = "yDeg" },
            new() { Source = "4", ParameterKey = "zDeg" }, new() { Source = "5", ParameterKey = "axG" },
            new() { Source = "6", ParameterKey = "ayG" }, new() { Source = "7", ParameterKey = "azG" },
            new() { Source = "8", ParameterKey = "temperature" }, new() { Source = "9", ParameterKey = "batteryLevel" },
            new() { Source = "10", Target = ConnectorTarget.Ignore }
        ]
    };

    /// <summary>The same sensor's 21-byte Modbus-style binary frame.</summary>
    private static Connector FgWm400Binary() => new()
    {
        Name = "FG-WM400 binary", Port = 12001, Framing = ConnectorFraming.FixedLength, FixedLength = 21, Format = ConnectorFormat.Binary,
        BinaryHeaderHex = "03 10", BinaryHeaderOffset = 1, BinaryChecksum = ConnectorChecksum.Crc16Modbus, DeviceCodePrefix = "fgwm400-",
        Mappings =
        [
            new() { Source = "0", Target = ConnectorTarget.SensorId, ByteLength = 1 },
            new() { Source = "3", ParameterKey = "xDeg", ByteLength = 2, Offset = 20000, Scale = 0.01 },
            new() { Source = "5", ParameterKey = "yDeg", ByteLength = 2, Offset = 20000, Scale = 0.01 },
            new() { Source = "7", ParameterKey = "zDeg", ByteLength = 2, Offset = 20000, Scale = 0.01 },
            new() { Source = "15", ParameterKey = "temperature", ByteLength = 2, Offset = 20000, Scale = 0.01 },
            new() { Source = "17", ParameterKey = "batteryLevel", ByteLength = 2 }
        ]
    };

    private static DecodedMessage Decode(Connector c, string text) => MessageDecoder.Decode(c, Encoding.UTF8.GetBytes(text));

    private static byte[] ModbusFrame(byte address)
    {
        var body = new byte[] { address, 0x03, 0x10 }.Concat(Convert.FromHexString("4EDF4E60707E4F6A4E8E74D359150009")).ToArray();
        var crc = MessageDecoder.Crc16Modbus(body);
        return [.. body, (byte)(crc & 0xFF), (byte)(crc >> 8)];
    }

    [Fact]
    public void Csv_ProTecSample_DecodesEveryField()
    {
        var m = Decode(FgWm400Text(), "100000005,+022.2149,+000.5842,+088.8487,+000.1818,+000.0289,+000.9814,+028.8700,7");

        Assert.True(m.Usable);
        Assert.Equal("100000005", m.SensorId);
        Assert.Equal(22.2149, m.Fields["xDeg"], 9);
        Assert.Equal(88.8487, m.Fields["zDeg"], 9);
        Assert.Equal(28.87, m.Fields["temperature"], 9);
        Assert.Equal(7, m.Fields["batteryLevel"]);
        Assert.Empty(m.UnmappedSources);
    }

    [Fact]
    public void Csv_WithPrefixAndPhone_SkipsPrefixAndIgnoresPhone()
    {
        var m = Decode(FgWm400Text(), "BW-NB-WM400,100000009,+001.0,+002.0,+088.0,+000.01,-000.0011,+000.99,+027.0,8,1440033931560");

        Assert.Equal("100000009", m.SensorId);
        Assert.Equal(-0.0011, m.Fields["ayG"], 9);
        Assert.False(m.Fields.ContainsKey("10"));
        Assert.Empty(m.UnmappedSources);
    }

    [Fact]
    public void Csv_NotANumber_IsNamedInTheError()
    {
        var m = Decode(FgWm400Text(), "100000005,abc,+000.58,+088.84,0,0,0,+028.87,7");

        Assert.False(m.Usable);
        Assert.Contains("column 2", m.Error);
    }

    [Theory]
    [InlineData(0x01, "1")]
    [InlineData(0x31, "49")] // looks like the text character "1" — Pro-Tec's server rejected this one
    public void Binary_AnyAddress_Decodes(byte address, string sensorId)
    {
        var m = MessageDecoder.Decode(FgWm400Binary(), ModbusFrame(address));

        Assert.True(m.Usable, m.Error);
        Assert.Equal(sensorId, m.SensorId);
        Assert.Equal(1.91, m.Fields["xDeg"], 9);
        Assert.Equal(87.98, m.Fields["zDeg"], 9);
        Assert.Equal(28.05, m.Fields["temperature"], 9);
        Assert.Equal(9, m.Fields["batteryLevel"]);
    }

    [Fact]
    public void Binary_CorruptedByte_FailsTheChecksum()
    {
        var frame = ModbusFrame(0x01);
        frame[5] ^= 0xFF;

        Assert.Contains("Checksum", MessageDecoder.Decode(FgWm400Binary(), frame).Error);
    }

    [Fact]
    public void Json_NestedPaths_MapAndReportUnmappedKeys()
    {
        var c = new Connector
        {
            Format = ConnectorFormat.Json, DeviceCodePrefix = "vx-",
            Mappings =
            [
                new() { Source = "sn", Target = ConnectorTarget.SensorId },
                new() { Source = "data.v", ParameterKey = "voltage" },
                new() { Source = "data.i", ParameterKey = "current" },
                new() { Source = "ts", Target = ConnectorTarget.Time }
            ]
        };

        var m = Decode(c, """{"sn":"7788123","data":{"v":230.5,"i":12.1},"rssi":-71,"ts":"2026-10-23T05:00:00Z"}""");

        Assert.Equal("7788123", m.SensorId);
        Assert.Equal(230.5, m.Fields["voltage"]);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 5, 0, 0, TimeSpan.Zero), m.Time);
        Assert.Equal(["rssi"], m.UnmappedSources);
    }

    [Fact]
    public void KeyValue_ExactKeys_WithScale()
    {
        var c = new Connector
        {
            Format = ConnectorFormat.KeyValue, PairSeparator = ";", KeyValueSeparator = "=", DeviceCodePrefix = "kv-",
            Mappings =
            [
                new() { Source = "ID", Target = ConnectorTarget.SensorId },
                new() { Source = "T", ParameterKey = "temperature", Scale = 0.1 },
                new() { Source = "t", ParameterKey = "other" }
            ]
        };

        var m = Decode(c, "ID=55;T=241;H=60");

        Assert.Equal("55", m.SensorId);
        Assert.Equal(24.1, m.Fields["temperature"], 9);
        Assert.False(m.Fields.ContainsKey("other")); // keys are case-sensitive: "t" isn't "T"
        Assert.Equal(["H"], m.UnmappedSources);
    }

    [Fact]
    public void DraftWithoutMapping_RejectsButStillListsTheValues()
    {
        var draft = new Connector { Format = ConnectorFormat.Json, DeviceCodePrefix = "tiltpro-" };

        var m = Decode(draft, """{"id":"TP-2001","tilt":{"x":12.5}}""");

        Assert.False(m.Usable);
        Assert.Equal(["id", "tilt.x"], m.UnmappedSources);
    }

    [Fact]
    public void MissingSensorId_IsNotUsable()
    {
        var m = Decode(FgWm400Text(), "");
        Assert.False(m.Usable);
    }
}
