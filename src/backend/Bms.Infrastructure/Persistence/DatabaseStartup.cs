using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bms.Infrastructure.Persistence;

public static class DatabaseStartup
{
    /// <summary>
    /// Applies pending migrations (then <paramref name="afterMigrate"/>, e.g.
    /// seeding), retrying with exponential backoff until PostgreSQL is
    /// reachable or <paramref name="ct"/> is cancelled — a database that is
    /// still starting or briefly down must delay this process, not kill it.
    /// </summary>
    public static async Task MigrateWithRetryAsync(
        IServiceProvider services, ILogger logger, CancellationToken ct, Func<IServiceProvider, Task>? afterMigrate = null)
    {
        var options = services.GetRequiredService<IOptions<PostgresResilienceOptions>>().Value;
        var delay = TimeSpan.FromSeconds(Math.Max(1, options.StartupRetryInitialDelaySeconds));
        var maxDelay = TimeSpan.FromSeconds(Math.Max(1, options.StartupRetryMaxDelaySeconds));

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BmsDbContext>();
                await db.Database.MigrateAsync(ct);
                if (afterMigrate is not null)
                    await afterMigrate(scope.ServiceProvider);

                if (attempt > 1)
                    logger.LogInformation("Database migration succeeded on attempt {Attempt}", attempt);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Database migration attempt {Attempt} failed (PostgreSQL unreachable?); retrying in {Delay}s",
                    attempt, delay.TotalSeconds);
                await Task.Delay(delay, ct);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxDelay.TotalSeconds));
            }
        }
    }
}
