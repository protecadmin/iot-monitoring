using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Notifications;

/// <summary>
/// Emails a tenant's admin-level users when a Critical alert fires — the
/// in-app bell/SignalR push (IRealtimeNotifier) only reaches someone
/// actively looking at the dashboard; this covers "nobody's watching right
/// now." Called from IngestionPipeline the same way IRealtimeNotifier is,
/// wrapped in the same TryAsync isolation so an SMTP outage can't break
/// ingestion. Implementation decides severity/recipient filtering — see
/// AlertEmailNotifier.
/// </summary>
public interface IAlertEmailNotifier
{
    Task NotifyAsync(Alert alert, Device device, CancellationToken ct);
}
