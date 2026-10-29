using Bms.Application.Abstractions.Alerting;
using Microsoft.Extensions.DependencyInjection;

namespace Bms.Alerting.DependencyInjection;

public static class AlertingServiceCollectionExtensions
{
    public static IServiceCollection AddBmsAlerting(this IServiceCollection services)
    {
        services.AddScoped<IAlertEvaluator, AlertEvaluator>();
        return services;
    }
}
