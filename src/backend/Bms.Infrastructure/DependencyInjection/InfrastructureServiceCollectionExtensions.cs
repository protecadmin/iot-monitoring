using Bms.Application.Tilt;
using Bms.Application.Abstractions.Ota;
using Bms.Application.Abstractions.Notifications;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Security;
using Bms.Application.Abstractions.Telemetry;
using Bms.Infrastructure.Ota;
using Bms.Infrastructure.Notifications;
using Bms.Infrastructure.Persistence;
using Bms.Infrastructure.Persistence.Repositories;
using Bms.Infrastructure.Security;
using Bms.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bms.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddBmsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PostgresResilienceOptions>(configuration.GetSection(PostgresResilienceOptions.SectionName));
        var resilience = configuration.GetSection(PostgresResilienceOptions.SectionName).Get<PostgresResilienceOptions>()
                         ?? new PostgresResilienceOptions();

        // Retries a failed command/SaveChanges on a transient error (server
        // restarting, connection dropped). A user-initiated transaction must
        // run inside db.Database.CreateExecutionStrategy() with this on.
        services.AddDbContext<BmsDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres"), npgsql => npgsql
                .EnableRetryOnFailure(
                    maxRetryCount: Math.Max(0, resilience.MaxRetryCount),
                    maxRetryDelay: TimeSpan.FromSeconds(Math.Max(1, resilience.MaxRetryDelaySeconds)),
                    errorCodesToAdd: null)
                .CommandTimeout(Math.Max(1, resilience.CommandTimeoutSeconds))));

        services.AddSingleton<ITransientFailureClassifier, TransientFailureClassifier>();

        services.Configure<InfluxDbOptions>(configuration.GetSection(InfluxDbOptions.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));
        services.Configure<FirmwareOptions>(configuration.GetSection(FirmwareOptions.SectionName));

        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<ISiteRepository, SiteRepository>();
        services.AddScoped<IGatewayRepository, GatewayRepository>();
        services.AddScoped<IDeviceTypeRepository, DeviceTypeRepository>();
        services.AddScoped<IDeviceRepository, DeviceRepository>();
        services.AddScoped<IDeviceBaselineRepository, DeviceBaselineRepository>();
        services.AddScoped<IConnectorRepository, ConnectorRepository>();
        services.AddScoped<ICounterResetRepository, CounterResetRepository>();
        services.AddScoped<IGlobalAlertRuleRepository, GlobalAlertRuleRepository>();
        services.AddScoped<IAlertRuleRepository, AlertRuleRepository>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<IDeviceCommandRepository, DeviceCommandRepository>();
        services.AddScoped<IDeviceConfigurationRepository, DeviceConfigurationRepository>();
        services.AddScoped<IFirmwareRepository, FirmwareRepository>();
        services.AddScoped<IFirmwareDeploymentRepository, FirmwareDeploymentRepository>();
        services.AddScoped<IRawMessageRepository, RawMessageRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IMqttCredentialRepository, MqttCredentialRepository>();
        services.AddScoped<ISmtpSettingsRepository, SmtpSettingsRepository>();
        services.AddScoped<ITelegramSettingsRepository, TelegramSettingsRepository>();
        services.AddScoped<ICalculationRuleRepository, CalculationRuleRepository>();
        services.AddScoped<ICalculationStateRepository, CalculationStateRepository>();
        services.AddScoped<IDeviceCalculationAssignmentRepository, DeviceCalculationAssignmentRepository>();

        // One Influx client for both: derived series are computed from the same raw
        // telemetry the store writes, so they read the same bucket.
        services.AddSingleton<InfluxTelemetryStore>();
        services.AddSingleton<ITiltTelemetryReader>(sp => sp.GetRequiredService<InfluxTelemetryStore>());
        services.AddScoped<ITiltMonitoringService, TiltMonitoringService>();
        services.AddSingleton<ITelemetryStore>(sp => sp.GetRequiredService<InfluxTelemetryStore>());
        services.AddSingleton<ICalculatedSeriesStore>(sp => sp.GetRequiredService<InfluxTelemetryStore>());
        services.AddSingleton<IPasswordHasher, Rfc2898PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IMqttCredentialHasher, BcryptMqttCredentialHasher>();
        services.AddSingleton<IReversibleEncryptor, AesReversibleEncryptor>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IAlertEmailNotifier, AlertEmailNotifier>();

        services.AddHttpClient(nameof(TelegramSender));
        services.AddScoped<ITelegramSender, TelegramSender>();
        services.AddScoped<IAlertTelegramNotifier, AlertTelegramNotifier>();


        services.AddSingleton<IFirmwareStorage, LocalFirmwareStorage>();
        services.AddSingleton<IFirmwareDownloadLinks, FirmwareDownloadLinks>();

        return services;
    }
}
