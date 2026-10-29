namespace Bms.Ingestion.Workers;

/// <summary>
/// This process's own view of whether it is recording messages: degraded
/// while a message is stuck retrying on a transient failure (PostgreSQL
/// down), healthy again on the next success. Published by
/// IngestionStatusPublisher.
/// </summary>
public sealed class IngestionHealthState(TimeProvider clock)
{
    private readonly object _gate = new();
    private string? _degradedReason;
    private DateTimeOffset? _healthySince = clock.GetUtcNow();

    public (bool Degraded, string? Reason, DateTimeOffset? HealthySince) Snapshot()
    {
        lock (_gate)
            return (_degradedReason is not null, _degradedReason, _healthySince);
    }

    public void MarkDegraded(string reason)
    {
        lock (_gate)
        {
            _degradedReason = reason;
            _healthySince = null;
        }
    }

    public void MarkHealthy()
    {
        lock (_gate)
        {
            if (_degradedReason is null && _healthySince is not null)
                return;
            _degradedReason = null;
            _healthySince = clock.GetUtcNow();
        }
    }

    /// <summary>A (re)connect to the broker starts a new healthy period: a queued backlog is about to be drained.</summary>
    public void MarkReconnected()
    {
        lock (_gate)
        {
            if (_degradedReason is null)
                _healthySince = clock.GetUtcNow();
        }
    }
}
