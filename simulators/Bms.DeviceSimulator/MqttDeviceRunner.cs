using System.Buffers;
using System.Text.Json;
using MQTTnet;

namespace Bms.DeviceSimulator;

/// <summary>
/// Runs one MQTT-connected simulated device: publishes telemetry and
/// heartbeat, and responds to commands/config/firmware pushes — the full
/// bidirectional flow in core.txt sections 40-42. Every device reports over
/// MQTT, as our own gateway does.
/// </summary>
public sealed class MqttDeviceRunner
{
    private readonly DeviceProfile _profile;
    private readonly SimulatorOptions _options;
    private readonly TelemetryState _state;
    private readonly IMqttClient _client;
    private static readonly HttpClient FirmwareHttp = new() { Timeout = TimeSpan.FromMinutes(2) };
    private int _reportingIntervalSeconds;
    private int _heartbeatIntervalSeconds;

    public MqttDeviceRunner(DeviceProfile profile, SimulatorOptions options)
    {
        _profile = profile;
        _options = options;
        _state = new TelemetryState(profile.DeviceCode.GetHashCode());
        _reportingIntervalSeconds = options.IntervalSeconds;
        _heartbeatIntervalSeconds = options.HeartbeatIntervalSeconds;
        _client = new MqttClientFactory().CreateMqttClient();
    }

    private string Topic(string messageType) => $"iot/{_profile.TenantSlug}/{_profile.SiteCode}/{_profile.DeviceCode}/{messageType}";

    public async Task RunAsync(CancellationToken ct)
    {
        _client.ApplicationMessageReceivedAsync += OnMessageAsync;

        await ConnectWithRetryAsync(ct);

        var telemetryTask = TelemetryLoopAsync(ct);
        var heartbeatTask = HeartbeatLoopAsync(ct);

        await Task.WhenAll(telemetryTask, heartbeatTask);
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var options = new MqttClientOptionsBuilder()
                    .WithClientId($"sim-{_profile.DeviceCode}")
                    .WithTcpServer(_options.MqttHost, _options.MqttPort)
                    // The broker's ACL (infrastructure/mosquitto/acl) restricts
                    // this username's publish/subscribe to iot/{tenantSlug}/# —
                    // authenticating as anything but this device's own tenant
                    // would be rejected, not just "in bad taste".
                    .WithCredentials(_profile.TenantSlug, _options.MqttPassword)
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                    .WithCleanSession(false)
                    .WithWillTopic(Topic("status"))
                    .WithWillPayload("offline")
                    .WithWillRetain(true)
                    .Build();

                await _client.ConnectAsync(options, ct);
                foreach (var messageType in new[] { "commands", "config", "firmware" })
                {
                    var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(f => f.WithTopic(Topic(messageType)).WithAtLeastOnceQoS())
                        .Build();
                    await _client.SubscribeAsync(subscribeOptions, ct);
                }

                Console.WriteLine($"[{_profile.DeviceCode}] connected to MQTT {_options.MqttHost}:{_options.MqttPort}");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{_profile.DeviceCode}] MQTT connect failed ({ex.Message}), retrying in {delay.TotalSeconds}s");
                await Task.Delay(delay, ct);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }

    private async Task TelemetryLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_reportingIntervalSeconds), ct);
                _state.Tick();

                if (_options.DelayMs > 0)
                    await Task.Delay(_options.DelayMs, ct);

                var payload = FailureInjection.MaybeCorrupt(BuildTelemetryPayload(), _options.MalformedRate);
                await PublishAsync(Topic("telemetry"), payload, ct);

                if (FailureInjection.ShouldDuplicate(_options.DuplicateRate))
                {
                    Console.WriteLine($"[{_profile.DeviceCode}] resending duplicate (seq {_state.Sequence})");
                    await PublishAsync(Topic("telemetry"), payload, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Console.WriteLine($"[{_profile.DeviceCode}] telemetry publish failed: {ex.Message}");
            }
        }
    }

    private byte[] BuildTelemetryPayload() => PayloadEncoders.Gateway(_profile, _state);

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_heartbeatIntervalSeconds), ct);
                await PublishAsync(Topic("heartbeat"), "{}"u8.ToArray(), ct);
            }
            catch (OperationCanceledException) { }
        }
    }

    private async Task PublishAsync(string topic, byte[] payload, CancellationToken ct)
    {
        if (!_client.IsConnected)
        {
            Console.WriteLine($"[{_profile.DeviceCode}] not connected, dropping publish to {topic}");
            return;
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        await _client.PublishAsync(message, ct);
    }

    private async Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var topic = args.ApplicationMessage.Topic;
        var payload = ToByteArray(args.ApplicationMessage.Payload);

        try
        {
            if (topic == Topic("commands"))
                await HandleCommandAsync(payload);
            else if (topic == Topic("config"))
                HandleConfig(payload);
            else if (topic == Topic("firmware"))
                await HandleFirmwareAsync(payload);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{_profile.DeviceCode}] failed handling message on {topic}: {ex.Message}");
        }
    }

    private async Task HandleCommandAsync(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var commandId = doc.RootElement.GetProperty("commandId").GetString();
        var type = doc.RootElement.GetProperty("type").GetString();
        Console.WriteLine($"[{_profile.DeviceCode}] received command {type} ({commandId})");

        await Task.Delay(TimeSpan.FromMilliseconds(300));

        if (type == "RequestTelemetry")
        {
            _state.Tick();
            await PublishAsync(Topic("telemetry"), BuildTelemetryPayload(), CancellationToken.None);
        }
        else if (type == "ChangeReportingInterval" && doc.RootElement.TryGetProperty("payload", out var p) &&
                 p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var newInterval))
        {
            _reportingIntervalSeconds = newInterval;
        }

        var status = Random.Shared.NextDouble() < 0.05 ? "FAILED" : "ACKNOWLEDGED";
        var ack = JsonSerializer.SerializeToUtf8Bytes(new { commandId, status });
        await PublishAsync(Topic("commands/ack"), ack, CancellationToken.None);
    }

    private void HandleConfig(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload);
        if (doc.RootElement.TryGetProperty("reportingInterval", out var interval))
        {
            _reportingIntervalSeconds = interval.GetInt32();
            Console.WriteLine($"[{_profile.DeviceCode}] applied new reporting interval: {_reportingIntervalSeconds}s");
        }
        // Takes effect after the heartbeat already waiting finishes — the
        // loop reads the field fresh on every cycle.
        if (doc.RootElement.TryGetProperty("heartbeatInterval", out var heartbeat) && heartbeat.TryGetInt32(out var hb) && hb > 0)
        {
            _heartbeatIntervalSeconds = hb;
            Console.WriteLine($"[{_profile.DeviceCode}] applied new heartbeat interval: {_heartbeatIntervalSeconds}s");
        }
    }

    /// <summary>
    /// FOTA is software-side simulation only (core.txt section 42) — no
    /// real firmware, bootloader, or flashing. Just the state machine and
    /// timing a real deployment would go through, reported back over
    /// the "events" topic as {"eventType":"fota", ...}.
    /// </summary>
    private async Task HandleFirmwareAsync(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var deploymentId = root.GetProperty("deploymentId").GetString();
        var version = root.GetProperty("version").GetString();
        Console.WriteLine($"[{_profile.DeviceCode}] FOTA deployment {deploymentId} -> v{version} starting");

        async Task ReportAsync(string status, string? error = null, string? firmwareVersion = null)
        {
            var evt = JsonSerializer.SerializeToUtf8Bytes(new { eventType = "fota", deploymentId, status, error, firmwareVersion });
            await PublishAsync(Topic("events"), evt, CancellationToken.None);
            Console.WriteLine($"[{_profile.DeviceCode}] FOTA {deploymentId}: {status}{(error is null ? "" : $" ({error})")}");
        }

        await ReportAsync("DOWNLOADING");

        // A real release carries a signed download link: fetch the binary and
        // check it against the checksum in the message, exactly as a real
        // device must. Older releases without a file fall back to the
        // original simulated step (with its occasional injected failure).
        byte[]? binary = null;
        if (root.TryGetProperty("downloadUrl", out var urlProp) && urlProp.GetString() is { Length: > 0 } downloadUrl)
        {
            try
            {
                using var response = await FirmwareHttp.GetAsync(downloadUrl);
                if (!response.IsSuccessStatusCode)
                {
                    await ReportAsync("FAILED", $"Download failed: HTTP {(int)response.StatusCode}");
                    return;
                }
                binary = await response.Content.ReadAsByteArrayAsync();
                Console.WriteLine($"[{_profile.DeviceCode}] FOTA {deploymentId}: downloaded {binary.Length:N0} bytes");
            }
            catch (Exception ex)
            {
                await ReportAsync("FAILED", $"Download failed: {ex.Message}");
                return;
            }
        }
        else
        {
            await Task.Delay(TimeSpan.FromSeconds(1.5));
        }

        await ReportAsync("VERIFYING");
        await Task.Delay(TimeSpan.FromSeconds(0.7));
        if (binary is not null)
        {
            var expected = root.TryGetProperty("sha256Checksum", out var sumProp) ? sumProp.GetString() : null;
            var actual = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(binary));
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                await ReportAsync("FAILED", "Checksum verification failed — the downloaded file does not match");
                return;
            }
        }
        else if (Random.Shared.NextDouble() < 0.08)
        {
            await ReportAsync("FAILED", "Checksum verification failed");
            return;
        }

        await ReportAsync("INSTALLING");
        await Task.Delay(TimeSpan.FromSeconds(1));

        await ReportAsync("REBOOTING");
        await _client.DisconnectAsync(); // simulate a real device restart, including the broker connection dropping
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        await ConnectWithRetryAsync(CancellationToken.None);

        await ReportAsync("SUCCESS", firmwareVersion: version);
    }

    private static byte[] ToByteArray(ReadOnlySequence<byte> sequence)
    {
        if (sequence.IsSingleSegment)
            return sequence.FirstSpan.ToArray();
        var array = new byte[sequence.Length];
        sequence.CopyTo(array);
        return array;
    }
}
