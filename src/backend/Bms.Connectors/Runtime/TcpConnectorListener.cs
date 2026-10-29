using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Bms.Connectors.Core;
using Bms.Domain.Connectors;
using Microsoft.Extensions.Options;

namespace Bms.Connectors.Runtime;

/// <summary>
/// Listens on one connector's TCP port. Each connection gets its own framer,
/// so a message split across reads is reassembled. Nothing is ever written
/// back: these devices don't expect a reply.
/// </summary>
public sealed class TcpConnectorListener(
    Connector connector,
    MessageProcessor processor,
    ServiceStats stats,
    IOptions<ConnectorServiceOptions> options,
    ILogger logger) : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<IPAddress, int> PerIp = new();
    private static int _total;

    private readonly CancellationTokenSource _stop = new();
    private Task? _run;

    public Connector Connector => connector;

    public void Start()
    {
        var listener = new TcpListener(IPAddress.Parse(options.Value.BindAddress), connector.Port);
        listener.Start(256);
        stats.For(connector).Listening = true;
        logger.LogInformation("Connector {Name} listening on TCP {Port} ({Format})", connector.Name, connector.Port, connector.Format);
        _run = AcceptLoopAsync(listener, _stop.Token);
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(ct); }
                catch (OperationCanceledException) { break; }

                var ip = (client.Client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None;
                if (Interlocked.Increment(ref _total) > options.Value.MaxConnections
                    || PerIp.AddOrUpdate(ip, 1, (_, n) => n + 1) > options.Value.MaxConnectionsPerIp)
                {
                    Release(ip);
                    client.Dispose();
                    stats.Reject(connector, $"connection limit reached; refused {ip}");
                    continue;
                }
                Interlocked.Increment(ref stats.For(connector).Connections);
                _ = HandleAsync(client, ip, ct);
            }
        }
        finally
        {
            listener.Stop();
            stats.For(connector).Listening = false;
        }
    }

    private async Task HandleAsync(TcpClient client, IPAddress ip, CancellationToken ct)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        var framer = new MessageFramer(connector);
        var buffer = new byte[4096];
        var gotData = false;
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                while (!ct.IsCancellationRequested)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    // Quick to give up on a connection that never says anything; patient once a device is talking.
                    timeout.CancelAfter(TimeSpan.FromSeconds(gotData ? options.Value.IdleTimeoutSeconds : options.Value.FirstByteTimeoutSeconds));
                    int read;
                    try { read = await stream.ReadAsync(buffer, timeout.Token); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { break; }
                    if (read == 0) break;
                    gotData = true;
                    foreach (var message in framer.Feed(buffer.AsSpan(0, read)))
                        processor.Process(connector, message, remote);
                }
                if (framer.Flush() is { } last && !ct.IsCancellationRequested)
                    processor.Process(connector, last, remote);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            logger.LogDebug(ex, "Connection from {Remote} on {Name} ended", remote, connector.Name);
        }
        finally
        {
            Release(ip);
        }
    }

    private static void Release(IPAddress ip)
    {
        Interlocked.Decrement(ref _total);
        PerIp.AddOrUpdate(ip, 0, (_, n) => Math.Max(0, n - 1));
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_run is not null)
        {
            try { await _run; } catch (OperationCanceledException) { }
        }
        _stop.Dispose();
        logger.LogInformation("Connector {Name} stopped listening on TCP {Port}", connector.Name, connector.Port);
    }
}
