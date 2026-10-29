using Bms.Connectors;
using Bms.Connectors.Api;
using Bms.Connectors.Runtime;
using Bms.Connectors.Workers;
using Microsoft.Extensions.Options;

// The Connector Service: listens on each connector's port, translates
// whatever the devices send into BMS readings, and publishes them over MQTT.
// It holds no database of its own — connectors and devices come from the
// BMS API, which it polls.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<ConnectorServiceOptions>(builder.Configuration.GetSection(ConnectorServiceOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ServiceState>();
builder.Services.AddSingleton<ServiceStats>();

builder.Services.AddHttpClient<BmsApiClient>((sp, http) =>
{
    var o = sp.GetRequiredService<IOptions<ConnectorServiceOptions>>().Value;
    http.BaseAddress = new Uri(o.ApiBaseUrl.TrimEnd('/') + "/");
    http.DefaultRequestHeaders.Add(BmsApiClient.KeyHeader, o.ApiKey);
    http.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddSingleton<MqttPublisher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttPublisher>());
builder.Services.AddSingleton<MessageProcessor>();
builder.Services.AddHostedService<ListenerSupervisor>();
builder.Services.AddHostedService<Reporter>();

var host = builder.Build();
if (string.IsNullOrEmpty(host.Services.GetRequiredService<IOptions<ConnectorServiceOptions>>().Value.ApiKey))
    host.Services.GetRequiredService<ILogger<Program>>().LogWarning("ConnectorService:ApiKey is empty — the BMS API will refuse every call");
host.Run();
