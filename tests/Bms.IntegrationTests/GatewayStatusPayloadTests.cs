using System.Reflection;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Realtime;
using Bms.Application.Abstractions.Transports;
using Bms.Domain;
using Bms.Ingestion.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace Bms.IntegrationTests;

public class GatewayStatusPayloadTests
{
    // Exercises the actual ingestion handler without starting MQTT, HTTP or a database.
    [Fact]
    public async Task Status_live_event_preserves_complete_json_beyond_log_preview_limit()
    {
        var payload = JsonSerializer.Serialize(new
        {
            type = "status", device_id = "860738070588839",
            firmware = new { stm32 = "0.5.15-STABLE", esp32_code = 638 },
            sensor = new { name = "BSWA308", connected = true },
            padding = new string('x', 800),
            network = new { ip = "100.78.24.118", rssi_dbm = -69, gnss = new { fix = true, lat = 13.083180, lon = 80.220200 } },
            errors = new { measurement_queue_rejections = 0 }
        });
        var repository = DispatchProxy.Create<IGatewayRepository, RecordingProxy>();
        var notifier = DispatchProxy.Create<IRealtimeNotifier, RecordingProxy>();
        GatewayMessageUpdate? received = null;
        ((RecordingProxy)(object)notifier).OnCall = (method, args) =>
        {
            if (method.Name == nameof(IRealtimeNotifier.GatewayMessageReceivedAsync)) received = (GatewayMessageUpdate)args[0]!;
        };
        using var services = new ServiceCollection().AddSingleton(repository).AddSingleton(notifier).BuildServiceProvider();
        var message = new TransportMessage(TransportType.Mqtt, "protec/gateway/860738070588839/status", Encoding.UTF8.GetBytes(payload), DateTimeOffset.UtcNow);
        var handler = typeof(MqttIngestionWorker).GetMethod("HandleGatewayStatusAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

        await (Task)handler.Invoke(null, [services, "860738070588839", message, CancellationToken.None])!;

        Assert.NotNull(received);
        Assert.Equal(message.Address, received.Topic);
        Assert.Equal(payload, received.Payload);
        Assert.True(Encoding.UTF8.GetByteCount(received.Payload) > 512);
        using var json = JsonDocument.Parse(received.Payload);
        Assert.Equal(-69, json.RootElement.GetProperty("network").GetProperty("rssi_dbm").GetInt32());
        Assert.True(json.RootElement.GetProperty("network").GetProperty("gnss").GetProperty("fix").GetBoolean());
        Assert.Equal(0, json.RootElement.GetProperty("errors").GetProperty("measurement_queue_rejections").GetInt32());
    }

    public class RecordingProxy : DispatchProxy
    {
        public Action<MethodInfo, object?[]>? OnCall { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            OnCall?.Invoke(targetMethod!, args!);
            return Task.CompletedTask;
        }
    }
}
