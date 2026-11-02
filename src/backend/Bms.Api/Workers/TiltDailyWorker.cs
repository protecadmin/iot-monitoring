using Bms.Application.Tilt;
using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bms.Api.Workers;

public sealed class TiltDailyWorker(IServiceScopeFactory scopes,ILogger<TiltDailyWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                List<Guid> ids;
                using(var scope=scopes.CreateScope())ids=await scope.ServiceProvider.GetRequiredService<BmsDbContext>().TiltMonitoringSettings.AsNoTracking().Select(s=>s.DeviceId).ToListAsync(stoppingToken);
                foreach(var id in ids)
                {
                    try { using var scope=scopes.CreateScope();await scope.ServiceProvider.GetRequiredService<ITiltMonitoringService>().ProcessDeviceAsync(id,stoppingToken); }
                    catch(Exception ex) when(!stoppingToken.IsCancellationRequested){logger.LogError(ex,"Daily tilt processing failed for {DeviceId}; will retry",id);}
                }
            }
            catch(Exception ex) when(!stoppingToken.IsCancellationRequested){logger.LogError(ex,"Daily tilt worker failed; will retry");}
            try{await Task.Delay(TimeSpan.FromSeconds(30),stoppingToken);}catch(OperationCanceledException){break;}
        }
    }
}
