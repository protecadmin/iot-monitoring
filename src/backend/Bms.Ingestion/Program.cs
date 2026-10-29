using Bms.Alerting.DependencyInjection;
using Bms.Calculations.DependencyInjection;
using Bms.Application.Abstractions.Realtime;
using Bms.Application.Ingestion;
using Bms.DeviceManagement.DependencyInjection;
using Bms.Infrastructure.DependencyInjection;
using Bms.Ingestion.Workers;
using Bms.Protocols.DependencyInjection;
using Bms.Protocols.Realtime;
using Bms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBmsInfrastructure(builder.Configuration);
builder.Services.AddBmsProtocols(builder.Configuration);
builder.Services.AddBmsAlerting();
builder.Services.AddBmsCalculations();
builder.Services.AddBmsDeviceManagement(builder.Configuration);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<IngestionReliabilityOptions>(builder.Configuration.GetSection(IngestionReliabilityOptions.SectionName));
builder.Services.Configure<TimeSeriesBackfillOptions>(builder.Configuration.GetSection(TimeSeriesBackfillOptions.SectionName));
builder.Services.Configure<IngestionStatusOptions>(builder.Configuration.GetSection(IngestionStatusOptions.SectionName));
builder.Services.Configure<RawMessageRetentionOptions>(builder.Configuration.GetSection(RawMessageRetentionOptions.SectionName));

// The reliability counters have existed since the pipeline was written, but
// nothing read them outside a local `dotnet-counters` session — so an outage
// lasted until a user complained. This exposes them for scraping on a port
// of their own (the worker has no web server), which is what turns
// "something is wrong" into an alert.
var metricsPort = builder.Configuration.GetValue("Metrics:PrometheusPort", 9464);
if (metricsPort > 0)
{
    // Kestrel rather than the exporter's own HttpListener: on this platform
    // that listener accepts neither "*"/"+" (the exporter parses the host as
    // a System.Uri, which rejects both) nor "0.0.0.0" — and when it cannot
    // bind it throws during host start, taking ingestion down with it. A
    // metrics endpoint must never be able to do that.
    builder.WebHost.UseUrls($"http://0.0.0.0:{metricsPort}");
    builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics
        .AddMeter(IngestionMetrics.MeterName)
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());
}
else
{
    // Metrics off: bind an ephemeral loopback port so the web host has
    // somewhere to live without claiming anything meaningful.
    builder.WebHost.UseUrls("http://127.0.0.1:0");
}

builder.Services.AddScoped<IIngestionPipeline, IngestionPipeline>();
builder.Services.AddScoped<TelemetryNormalizer>();
builder.Services.AddScoped<TimeSeriesBackfillService>();
builder.Services.AddScoped<RawMessageRetentionService>();
builder.Services.AddSingleton<IngestionHealthState>();

// This process has no SignalR hub of its own — real-time events are
// bridged to Bms.Api over MQTT. See MqttRealtimeNotifier.
builder.Services.AddScoped<IRealtimeNotifier, MqttRealtimeNotifier>();

builder.Services.AddHostedService<MqttIngestionWorker>();
builder.Services.AddHostedService<TimeSeriesBackfillWorker>();
builder.Services.AddHostedService<RawMessageRetentionWorker>();
// Last: stopped first, so it can publish "offline" while still connected.
builder.Services.AddHostedService<IngestionStatusPublisher>();

// OfflineSweepWorker / TimeoutSweepWorker deliberately run in Bms.Api, not
// here — they only need DB access, not any MQTT/TCP listening machinery,
// and Bms.Api is the process guaranteed to be up whenever the dashboard
// is usable. Colocating them with this process's socket listeners meant
// stopping Bms.Ingestion (which does restart/crash independently of the
// API — it owns the flaky external connections) silently froze every
// device's status forever, with no signal that monitoring itself had
// stopped. See Bms.Api/Program.cs.

var host = builder.Build();

// PostgreSQL briefly unavailable at startup (still booting, restarting)
// delays this process instead of crashing it; Ctrl+C/SIGTERM still exits.
using (var startupCts = new CancellationTokenSource())
{
    ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; startupCts.Cancel(); };
    Console.CancelKeyPress += cancel;
    using var sigterm = System.Runtime.InteropServices.PosixSignalRegistration.Create(
        System.Runtime.InteropServices.PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; startupCts.Cancel(); });
    try
    {
        await DatabaseStartup.MigrateWithRetryAsync(
            host.Services, host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Bms.Ingestion.Startup"), startupCts.Token);
    }
    catch (OperationCanceledException)
    {
        return;
    }
    finally
    {
        Console.CancelKeyPress -= cancel;
    }
}

if (metricsPort > 0)
    host.MapPrometheusScrapingEndpoint();

host.Run();
