using Bms.Application.Abstractions.DeviceManagement;
using Bms.DeviceManagement.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bms.DeviceManagement.DependencyInjection;

public static class DeviceManagementServiceCollectionExtensions
{
    public static IServiceCollection AddBmsDeviceManagement(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DeviceLifecycleOptions>(configuration.GetSection(DeviceLifecycleOptions.SectionName));
        services.AddScoped<IDeviceLifecycleService, DeviceLifecycleService>();
        return services;
    }
}
