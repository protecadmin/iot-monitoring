namespace Bms.Application.Abstractions.Transports;

/// <summary>
/// Connection-lifecycle boundary for one transport (MQTT, TCP, HTTP).
/// Carries no business logic, alert logic, or database logic — see
/// docs/architecture/system-architecture.md.
/// </summary>
public interface ITransportAdapter : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    Task SendAsync(string address, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);

    /// <summary>Raised for every complete inbound message.</summary>
    event Func<TransportMessage, CancellationToken, Task>? MessageReceived;
}
