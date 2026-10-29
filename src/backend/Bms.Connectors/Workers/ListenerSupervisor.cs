using System.Net.Sockets;
using Bms.Connectors.Api;
using Bms.Connectors.Runtime;
using Bms.Domain.Connectors;
using Microsoft.Extensions.Options;

namespace Bms.Connectors.Workers;

/// <summary>
/// Keeps listeners in line with BMS: reads connectors and devices every
/// minute, starts a listener for a new connector, restarts one whose settings
/// changed, and stops one that was disabled or deleted — no service restart.
/// When BMS is unreachable it keeps running on the last config it had.
/// </summary>
public sealed class ListenerSupervisor(
    BmsApiClient api,
    ServiceState state,
    ServiceStats stats,
    MessageProcessor processor,
    IOptions<ConnectorServiceOptions> options,
    ILoggerFactory loggers,
    ILogger<ListenerSupervisor> logger) : BackgroundService
{
    private readonly Dictionary<Guid, TcpConnectorListener> _listeners = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var config = await api.GetConfigAsync(stoppingToken);
                state.Apply(config);
                await ReconcileAsync(config.Connectors.Where(c => c.IsEnabled && c.Protocol == ConnectorProtocol.Tcp).ToList());
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                stats.LastError = $"Config: {ex.Message}";
                logger.LogWarning("Couldn't read connectors from BMS ({Message}); keeping the current ones", ex.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.Value.ConfigRefreshSeconds), stoppingToken);
        }

        foreach (var listener in _listeners.Values)
            await listener.DisposeAsync();
    }

    private async Task ReconcileAsync(IReadOnlyList<Connector> wanted)
    {
        var wantedById = wanted.ToDictionary(c => c.Id);

        // Stop what is gone or changed (a change restarts it with the new settings).
        foreach (var (id, listener) in _listeners.ToList())
        {
            if (wantedById.TryGetValue(id, out var current) && current.UpdatedAt == listener.Connector.UpdatedAt)
                continue;
            await listener.DisposeAsync();
            _listeners.Remove(id);
        }

        foreach (var connector in wanted.Where(c => !_listeners.ContainsKey(c.Id)))
        {
            var listener = new TcpConnectorListener(connector, processor, stats, options, loggers.CreateLogger($"Connector.{connector.Port}"));
            try
            {
                listener.Start();
                _listeners[connector.Id] = listener;
            }
            catch (SocketException ex)
            {
                // e.g. the port is in use by something else — retried on the next refresh.
                stats.Reject(connector, $"can't listen on TCP {connector.Port}: {ex.Message}");
                logger.LogError("Connector {Name} can't listen on TCP {Port}: {Message}", connector.Name, connector.Port, ex.Message);
            }
        }
    }
}
