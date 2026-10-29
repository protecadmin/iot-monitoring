using Bms.Application.Abstractions.Ota;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Realtime;
using Bms.Application.Abstractions.Security;
using Bms.Application.Ota;
using Bms.Domain;
using Bms.Infrastructure.Ota;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bms.Ingestion.Workers;

/// <summary>
/// Marks commands and FOTA deployments that were sent but never
/// acknowledged/completed within a reasonable window as TIMEOUT, so a
/// device that silently drops off the network doesn't leave a command
/// stuck as "Sent" forever. See core.txt sections 37/39.
///
/// Also sends firmware deployments that were waiting for an Offline device
/// once it is Online again, and gives up on ones whose device never returns.
/// Hosted by Bms.Api (see its Program.cs), which holds the MQTT connection
/// the send needs.
/// </summary>
public sealed class TimeoutSweepWorker(
    IServiceScopeFactory scopeFactory, IOptions<FirmwareOptions> firmwareOptions, ILogger<TimeoutSweepWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            var notifier = services.GetRequiredService<IRealtimeNotifier>();

            try
            {
                var commands = services.GetRequiredService<IDeviceCommandRepository>();
                foreach (var command in await commands.ListTimedOutAsync(CommandTimeout, stoppingToken))
                {
                    command.Status = CommandStatus.Timeout;
                    command.Error = $"No acknowledgement within {CommandTimeout.TotalMinutes} minutes";
                    await commands.UpdateAsync(command, stoppingToken);
                    await notifier.CommandStatusChangedAsync(command, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Command timeout sweep failed");
            }

            try
            {
                var deployments = services.GetRequiredService<IFirmwareDeploymentRepository>();
                var firmwareTimeout = TimeSpan.FromMinutes(Math.Max(1, firmwareOptions.Value.CompletionTimeoutMinutes));
                foreach (var deployment in await deployments.ListTimedOutAsync(firmwareTimeout, stoppingToken))
                {
                    deployment.Status = FirmwareDeploymentStatus.Timeout;
                    deployment.Error = $"No completion within {firmwareTimeout.TotalMinutes} minutes";
                    deployment.CompletedAt = DateTimeOffset.UtcNow;
                    await deployments.UpdateAsync(deployment, stoppingToken);
                    await notifier.FirmwareDeploymentUpdatedAsync(deployment, stoppingToken);
                }

                // Waiting deployments whose device is back: send them now.
                var dispatcher = services.GetRequiredService<IDeviceCommandDispatcher>();
                var storage = services.GetRequiredService<IFirmwareStorage>();
                foreach (var deployment in await deployments.ListReadyToResumeAsync(stoppingToken))
                {
                    if (!storage.Exists(deployment.FirmwareId))
                    {
                        deployment.Status = FirmwareDeploymentStatus.Failed;
                        deployment.Error = "The firmware file for this release is missing";
                        deployment.CompletedAt = DateTimeOffset.UtcNow;
                        await deployments.UpdateAsync(deployment, stoppingToken);
                        await notifier.FirmwareDeploymentUpdatedAsync(deployment, stoppingToken);
                        continue;
                    }

                    logger.LogInformation("Device {DeviceId} is Online — sending waiting firmware deployment {DeploymentId}", deployment.DeviceId, deployment.Id);
                    await FirmwareDelivery.SendAsync(deployment, deployment.Device!, deployment.Firmware!, dispatcher, deployments, notifier, stoppingToken);
                }

                // Waiting deployments whose device never came back.
                var waitWindow = TimeSpan.FromHours(Math.Max(1, firmwareOptions.Value.WaitForDeviceHours));
                foreach (var deployment in await deployments.ListWaitExpiredAsync(waitWindow, stoppingToken))
                {
                    deployment.Status = FirmwareDeploymentStatus.Timeout;
                    deployment.Error = $"Device did not come Online within {waitWindow.TotalHours} hours";
                    deployment.CompletedAt = DateTimeOffset.UtcNow;
                    await deployments.UpdateAsync(deployment, stoppingToken);
                    await notifier.FirmwareDeploymentUpdatedAsync(deployment, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Firmware deployment timeout sweep failed");
            }
        }
    }
}
