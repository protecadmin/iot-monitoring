using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Bms.Connectors.Api;
using Bms.Connectors.Runtime;
using Microsoft.Extensions.Options;

namespace Bms.Connectors.Workers;

/// <summary>
/// Tells BMS how the service is doing (heartbeat with counters), which sensor
/// IDs arrive unregistered, which message parts arrive unmapped, and which
/// connectors are waiting for a captured sample.
/// </summary>
public sealed class Reporter(BmsApiClient api, ServiceState state, ServiceStats stats, IOptions<ConnectorServiceOptions> options, ILogger<Reporter> logger) : BackgroundService
{
    private static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextReport = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            await PollCapturesAsync(stoppingToken);
            if (DateTimeOffset.UtcNow >= nextReport)
            {
                await ReportAsync(stoppingToken);
                nextReport = DateTimeOffset.UtcNow.AddSeconds(options.Value.HeartbeatSeconds);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.CapturePollSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PollCapturesAsync(CancellationToken ct)
    {
        try
        {
            var pending = await api.PendingCapturesAsync(ct);
            foreach (var capture in pending)
                state.PendingCaptures[capture.ConnectorId] = capture.Id;
            // Forget captures BMS no longer waits for.
            foreach (var (connectorId, captureId) in state.PendingCaptures)
                if (pending.All(p => p.Id != captureId))
                    state.PendingCaptures.TryRemove(connectorId, out _);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug("Capture poll failed: {Message}", ex.Message);
        }
    }

    private async Task ReportAsync(CancellationToken ct)
    {
        try
        {
            var connectors = stats.PerConnector.Select(kv => new
            {
                id = kv.Key, name = kv.Value.Name, port = kv.Value.Port, listening = kv.Value.Listening,
                accepted = kv.Value.Accepted, rejected = kv.Value.Rejected, unknown = kv.Value.Unknown,
                connections = kv.Value.Connections, lastMessageAt = kv.Value.LastMessageAt, lastError = kv.Value.LastError
            });
            await api.HeartbeatAsync(new HeartbeatDto(
                Version, stats.Accepted, stats.Rejected, stats.Unknown, stats.Published,
                stats.Dropped > 0 ? $"{stats.Dropped} reading(s) dropped while the broker was unreachable. {stats.LastError}" : stats.LastError,
                JsonSerializer.Serialize(connectors)), ct);

            await FlushAsync(stats.UnknownDevices, api.ReportUnknownDevicesAsync, ct);
            await FlushAsync(stats.UnmappedKeys, api.ReportUnmappedKeysAsync, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Couldn't report to BMS ({Message}); will retry", ex.Message);
        }
    }

    /// <summary>Sends what was seen since the last report; on failure it stays to be sent next time.</summary>
    private static async Task FlushAsync(
        ConcurrentDictionary<(Guid, string), Sighting> sightings,
        Func<IReadOnlyList<SightingDto>, CancellationToken, Task> send,
        CancellationToken ct)
    {
        var taken = new List<SightingDto>();
        foreach (var key in sightings.Keys.Take(500).ToList())
            if (sightings.TryRemove(key, out var s))
                taken.Add(new SightingDto(key.Item1, key.Item2, s.Count, s.LastSeenAt, s.Sample));
        if (taken.Count == 0)
            return;
        try
        {
            await send(taken, ct);
        }
        catch
        {
            foreach (var s in taken)
                ServiceStats.Sight(sightings, s.ConnectorId, s.Key, s.Sample, s.LastSeenAt);
            throw;
        }
    }
}
