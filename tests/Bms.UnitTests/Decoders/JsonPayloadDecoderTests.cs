using System.Text;
using Bms.Protocols.Decoders;
using Xunit;

namespace Bms.UnitTests.Decoders;

public class JsonPayloadDecoderTests
{
    private readonly JsonPayloadDecoder _decoder = new();

    [Fact]
    public void Decode_FlatVendorPayload_ProducesFlatFields()
    {
        var raw = Encoding.UTF8.GetBytes("""{"deviceId":"D001","temp":31.5,"hum":68}""");

        var result = _decoder.Decode(raw);

        Assert.Equal(31.5, result.Fields["temp"]);
        Assert.Equal(68d, result.Fields["hum"]);
        Assert.Equal("D001", result.Fields["deviceId"]);
    }

    [Fact]
    public void Decode_NestedVendorPayload_FlattensWithDotNotation()
    {
        // Vendor C's shape (core.txt section 13) — the whole reason this
        // decoder flattens nested objects instead of only reading top-level fields.
        var raw = Encoding.UTF8.GetBytes("""{"id":"D001","data":{"temperature":31.5,"humidity":68}}""");

        var result = _decoder.Decode(raw);

        Assert.Equal(31.5, result.Fields["data.temperature"]);
        Assert.Equal(68d, result.Fields["data.humidity"]);
    }

    [Fact]
    public void Decode_MalformedJson_ThrowsRatherThanReturningPartialData()
    {
        var raw = Encoding.UTF8.GetBytes("{ this is not valid json");

        Assert.ThrowsAny<Exception>(() => _decoder.Decode(raw));
    }

    [Fact]
    public void Decode_SequenceNumber_IsExtractedWhenPresent()
    {
        var raw = Encoding.UTF8.GetBytes("""{"temp":20,"seq":42}""");

        var result = _decoder.Decode(raw);

        Assert.Equal(42, result.SequenceNumber);
    }

    [Fact]
    public void Decode_ProtecEnvelope_ExtractsMeasurementsAndUnixTimestamp()
    {
        var raw = Encoding.UTF8.GetBytes("""{"v":1,"type":"measurement","device_id":"860738070588839","sequence":1734,"timestamp_utc":1793770440,"values":{"layout":"xyz","mode":"acceleration","x":0.1,"y":-0.2,"z":0.9},"units":{"x":"m/s2"}}""");

        var result = _decoder.Decode(raw);

        Assert.Equal("860738070588839", result.DeviceIdentifierHint);
        Assert.Equal(1734, result.SequenceNumber);
        Assert.Equal(0.1, result.Fields["x"]);
        Assert.Equal(-0.2, result.Fields["y"]);
        Assert.Equal(0.9, result.Fields["z"]);
        Assert.Equal(0.1, result.Fields["values.x"]);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793770440), result.DeviceTimestamp);
    }

    [Theory]
    [InlineData("""{"t":1,"ts":"2026-10-23T05:00:00Z","tsSource":"received"}""", true)]
    [InlineData("""{"t":1,"ts":"2026-10-23T05:00:00Z","tsSource":"RECEIVED"}""", true)]
    [InlineData("""{"t":1,"ts":"2026-10-23T05:00:00Z"}""", false)]
    [InlineData("""{"t":1,"ts":"2026-10-23T05:00:00Z","tsSource":"device"}""", false)]
    public void Decode_TsSourceReceived_MarksTimestampAsArrival(string json, bool arrival) =>
        Assert.Equal(arrival, _decoder.Decode(Encoding.UTF8.GetBytes(json)).TimestampIsArrival);
}
