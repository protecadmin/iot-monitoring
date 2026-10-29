using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Bms.Connectors;
using Bms.Connectors.Api;
using Bms.Connectors.Core;
using Bms.Connectors.Runtime;
using Bms.Connectors.Workers;
using Bms.Domain.Connectors;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bms.UnitTests.Connectors;

/// <summary>The Connector Service from socket to publish queue, with a real TCP listener on a free local port.</summary>
public class ConnectorServiceTests
{
    private readonly ServiceState _state = new();
    private readonly ServiceStats _stats = new();
    private readonly IOptions<ConnectorServiceOptions> _options = Options.Create(new ConnectorServiceOptions { BindAddress = "127.0.0.1", FirstByteTimeoutSeconds = 2, IdleTimeoutSeconds = 2 });
    private readonly MqttPublisher _publisher;
    private readonly MessageProcessor _processor;
    private readonly Connector _connector;

    public ConnectorServiceTests()
    {
        _publisher = new MqttPublisher(_options, _stats, NullLogger<MqttPublisher>.Instance);
        var api = new BmsApiClient(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1/") });
        _processor = new MessageProcessor(_state, _stats, _publisher, api, _options, TimeProvider.System, NullLogger<MessageProcessor>.Instance);
        _connector = new Connector
        {
            Id = Guid.NewGuid(), Name = "FG-WM400", Port = FreePort(), Format = ConnectorFormat.Csv, Framing = ConnectorFraming.NewLine,
            CsvOptionalPrefix = true, DeviceCodePrefix = "fgwm400-",
            Mappings =
            [
                new() { Source = "1", Target = ConnectorTarget.SensorId },
                new() { Source = "2", ParameterKey = "xDeg" }, new() { Source = "3", ParameterKey = "yDeg" },
                new() { Source = "4", ParameterKey = "temperature" }, new() { Source = "5", ParameterKey = "batteryLevel" }
            ]
        };
        _state.Apply(new ServiceConfig([_connector], new Dictionary<(Guid, string), DeviceRoute>
        {
            [(_connector.Id, "100000005")] = new DeviceRoute("fgwm400-100000005", "protec", "amk-site-a")
        }));
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private async Task SendAsync(params string[] chunks)
    {
        await using var listener = new TcpConnectorListener(_connector, _processor, _stats, _options, NullLogger.Instance);
        listener.Start();
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, _connector.Port);
            var stream = client.GetStream();
            foreach (var chunk in chunks)
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(chunk));
                await Task.Delay(30);
            }
        }
        // Let the server side read to the end of the stream.
        for (var i = 0; i < 50 && _stats.Accepted + _stats.Rejected + _stats.Unknown == 0; i++) await Task.Delay(20);
        await Task.Delay(100);
    }

    private List<(string Topic, JsonElement Body)> Published()
    {
        var list = new List<(string, JsonElement)>();
        while (_publisher.TryDequeue(out var item)) list.Add((item.Topic, JsonDocument.Parse(item.Payload).RootElement));
        return list;
    }

    [Fact]
    public async Task RegisteredSensor_IsPublishedToItsCompanyTopic_WithArrivalTime()
    {
        await SendAsync("100000005,+022.2149,+000.5842,+028.87,7\n");

        var (topic, body) = Assert.Single(Published());
        Assert.Equal("iot/protec/amk-site-a/fgwm400-100000005/telemetry", topic);
        Assert.Equal(22.2149, body.GetProperty("xDeg").GetDouble(), 9);
        Assert.Equal(7, body.GetProperty("batteryLevel").GetDouble());
        Assert.Equal("received", body.GetProperty("tsSource").GetString()); // the sensor sends no time
        Assert.True(body.TryGetProperty("ts", out _));
        Assert.True(body.TryGetProperty("seq", out _));
    }

    [Fact]
    public async Task BurstSplitAcrossPackets_EveryReadingArrivesWithIncreasingSeq()
    {
        await SendAsync("100000005,+1.0,+2.0,+28.0,7\n100000005,+1.1,", "+2.0,+28.0,7\n100000005,+1.2,+2.0,+28.0,7");

        var published = Published();
        Assert.Equal(3, published.Count); // the last line had no newline — kept when the connection closed
        var seqs = published.Select(p => p.Body.GetProperty("seq").GetInt64()).ToList();
        Assert.Equal(seqs.OrderBy(s => s), seqs);
        Assert.Equal(1.2, published[2].Body.GetProperty("xDeg").GetDouble(), 9);
    }

    [Fact]
    public async Task UnregisteredSensor_IsNotPublished_ButReported()
    {
        await SendAsync("100000009,+1.0,+2.0,+28.0,7\n");

        Assert.Empty(Published());
        Assert.Equal(1, _stats.Unknown);
        Assert.True(_stats.UnknownDevices.ContainsKey((_connector.Id, "100000009")));
    }

    [Fact]
    public async Task ImmediateRepeat_IsDroppedAsADuplicate()
    {
        await SendAsync("100000005,+1.0,+2.0,+28.0,7\n100000005,+1.0,+2.0,+28.0,7\n");

        Assert.Single(Published());
    }

    [Fact]
    public async Task Garbage_IsRejectedWithAReason()
    {
        await SendAsync("hello there\n");

        Assert.Empty(Published());
        Assert.Equal(1, _stats.Rejected);
        Assert.NotNull(_stats.LastError);
    }
}
