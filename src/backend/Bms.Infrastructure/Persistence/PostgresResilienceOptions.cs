namespace Bms.Infrastructure.Persistence;

public sealed class PostgresResilienceOptions
{
    public const string SectionName = "PostgresResilience";

    /// <summary>EF Core retries of one database operation on a transient error before giving up to the caller.</summary>
    public int MaxRetryCount { get; set; } = 5;

    public int MaxRetryDelaySeconds { get; set; } = 10;

    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Startup migration: first wait after a failed attempt (doubles each time).</summary>
    public int StartupRetryInitialDelaySeconds { get; set; } = 2;

    public int StartupRetryMaxDelaySeconds { get; set; } = 60;
}
