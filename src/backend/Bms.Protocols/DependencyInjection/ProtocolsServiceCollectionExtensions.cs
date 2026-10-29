using Bms.Application.Abstractions.Payloads;
using Bms.Application.Abstractions.Protocols;
using Bms.Application.Abstractions.Security;
using Bms.Protocols.Configuration;
using Bms.Protocols.Dispatch;
using Bms.Protocols.Decoders;
using Bms.Protocols.Protocols;
using Bms.Protocols.Transports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bms.Protocols.DependencyInjection;

public static class ProtocolsServiceCollectionExtensions
{
    public static IServiceCollection AddBmsProtocols(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MqttOptions>(configuration.GetSection(MqttOptions.SectionName));

        services.AddSingleton<MqttTransportAdapter>();

        // Scoped, not singleton: these resolve IDeviceRepository, which is
        // scoped (EF Core DbContext lifetime). Callers that live outside a
        // request/message scope (the Ingestion worker's background
        // services) must open an IServiceScope per message.
        services.AddScoped<IProtocolHandler, MqttProtocolHandler>();

        // Devices report only over MQTT, as JSON, through our own gateway.
        services.AddSingleton<IPayloadDecoder, JsonPayloadDecoder>();

        services.AddScoped<IDeviceCommandDispatcher, MqttDeviceCommandDispatcher>();

        return services;
    }
}
