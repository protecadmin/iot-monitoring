namespace Bms.Application.Abstractions.Persistence;

/// <summary>
/// Decides whether a failure is an infrastructure outage that will pass
/// (database unreachable, timeout, connection reset) rather than something
/// wrong with the message itself. Ingestion must not acknowledge a message
/// that failed for a transient reason — the broker would then forget it —
/// and must not retry a message that failed for a permanent reason forever.
/// </summary>
public interface ITransientFailureClassifier
{
    bool IsTransient(Exception exception);
}
