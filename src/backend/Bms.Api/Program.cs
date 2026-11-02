using System.Text;
using Bms.Alerting.DependencyInjection;
using Bms.Calculations.DependencyInjection;
using Bms.Api.HealthChecks;
using Bms.Api.Middleware;
using Bms.Api.Realtime;
using Bms.Application.Abstractions.Realtime;
using Bms.Application.Ingestion;
using Bms.DeviceManagement.DependencyInjection;
using Bms.Infrastructure.DependencyInjection;
using Bms.Infrastructure.Persistence;
using Bms.Infrastructure.Security;
using Bms.Ingestion.Workers;
using Bms.Protocols.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "BMS Industrial IoT Platform API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT bearer token: \"Bearer {token}\"",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                { Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddBmsInfrastructure(builder.Configuration);
builder.Services.AddBmsProtocols(builder.Configuration);
builder.Services.AddBmsAlerting();
builder.Services.AddBmsCalculations();
builder.Services.AddBmsDeviceManagement(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
// A relative firmware folder means relative to this project, not to wherever
// `dotnet run` happened to be started from.
builder.Services.PostConfigure<Bms.Infrastructure.Ota.FirmwareOptions>(o =>
    o.StoragePath = Path.GetFullPath(o.StoragePath, builder.Environment.ContentRootPath));
builder.Services.AddScoped<IIngestionPipeline, IngestionPipeline>();
builder.Services.AddScoped<IRealtimeNotifier, RealtimeNotifier>();

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        // SignalR's JSON hub protocol has its own JsonSerializerOptions,
        // separate from ASP.NET Core MVC's — MVC already camelCases REST
        // responses by default, but without this, every SignalR payload
        // (built from anonymous objects like `new { alert.Id, alert.Severity }`
        // in RealtimeNotifier/MqttRealtimeNotifier) serializes with the
        // C# PascalCase property names instead. The frontend's TypeScript
        // types and `payload as AlertDto` casts assume camelCase, so
        // without this every field read from a live-pushed event came
        // back undefined (confirmed: REST-fetched alerts rendered fine,
        // SignalR-pushed ones showed blank rows).
        options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.Configure<Bms.Protocols.Realtime.IngestionHealthOptions>(
    builder.Configuration.GetSection(Bms.Protocols.Realtime.IngestionHealthOptions.SectionName));
builder.Services.AddSingleton(sp => new Bms.Protocols.Realtime.IngestionHealthTracker(
    sp.GetRequiredService<Bms.Protocols.Transports.MqttTransportAdapter>(),
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Bms.Protocols.Realtime.IngestionHealthOptions>>()));
builder.Services.AddSingleton<Bms.Application.Abstractions.DeviceManagement.IIngestionHealthMonitor>(
    sp => sp.GetRequiredService<Bms.Protocols.Realtime.IngestionHealthTracker>());
builder.Services.Configure<IngestionReliabilityOptions>(builder.Configuration.GetSection(IngestionReliabilityOptions.SectionName));
builder.Services.Configure<Bms.Api.Connectors.ConnectorOptions>(builder.Configuration.GetSection(Bms.Api.Connectors.ConnectorOptions.SectionName));
builder.Services.AddHostedService<Bms.Api.Realtime.RealtimeBridgeService>();

// Live here, not in Bms.Ingestion — see the comment in
// Bms.Ingestion/Program.cs for why these must not depend on that
// process's uptime.
builder.Services.AddHostedService<Bms.Api.Workers.TiltDailyWorker>();
builder.Services.AddHostedService<OfflineSweepWorker>();
builder.Services.AddHostedService<TimeoutSweepWorker>();

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000"];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Dashboard", policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(_ => { }); // options are bound below, lazily, from IConfiguration

// Bound via IConfigureOptions<T> rather than a local variable captured
// from builder.Configuration at this point in the file: reading
// configuration into a plain variable here snapshots it before some hosts
// (e.g. WebApplicationFactory-based integration tests, which layer
// additional config on after Program.cs starts running) finish applying
// their overrides — which previously meant tokens were *issued* using one
// Jwt:SecretKey value (read later, via IOptions, by JwtTokenService) and
// *validated* using a different, stale one, and every authenticated
// request failed with "signature key was not found". Binding through
// AddOptions<T>().Configure<IConfiguration>() defers the read until the
// options are actually resolved, by which point configuration is final.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, configuration) =>
    {
        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        var jwtSecret = jwtSection["SecretKey"];
        // The old `?? throw` here only caught a missing key, not an empty
        // string — appsettings.json's base config ships SecretKey as "",
        // meaning a deployment that forgets to set the real value via
        // environment variable/secrets manager would previously start up
        // "successfully" and sign every token with an empty key. A short
        // key is just as dangerous (brute-forceable HMAC-SHA256 signature)
        // so the length floor matters as much as the emptiness check.
        if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
            throw new InvalidOperationException(
                "Jwt:SecretKey is not configured (or is shorter than 32 characters). Set it via the Jwt__SecretKey " +
                "environment variable or a secrets manager — never in appsettings.json.");

        // Without this, the handler silently remaps short claim types
        // ("sub" etc.) to long legacy URIs, so User.FindFirst("sub") (used
        // throughout BmsControllerBase) would never match. Keep claims
        // exactly as JwtTokenService issued them.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // Let SignalR clients (which can't set an Authorization header on
        // the WebSocket handshake) pass the JWT as a query string value.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres")
    .AddCheck<InfluxDbHealthCheck>("influxdb")
    .AddCheck<MqttHealthCheck>("mqtt");

// Metrics for the same reason as in Bms.Ingestion: the counters exist, but
// an outage lasts until someone notices unless something scrapes them.
// Ingestion owns the telemetry counters; this side carries request latency
// and the runtime, which is what tells a slow dashboard apart from a slow
// database.
var metricsEnabled = builder.Configuration.GetValue("Metrics:Enabled", true);
if (metricsEnabled)
{
    builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // Retries until PostgreSQL is reachable instead of crashing on a
    // database that is still starting or briefly down.
    await DatabaseStartup.MigrateWithRetryAsync(app.Services, logger, app.Lifetime.ApplicationStopping, async services =>
        await DbSeeder.SeedAsync(
            services.GetRequiredService<BmsDbContext>(),
            services.GetRequiredService<Bms.Application.Abstractions.Security.IPasswordHasher>(),
            logger));

    // Doesn't block startup (unlike the Jwt:SecretKey check) since a
    // wrong CORS origin only breaks the dashboard, not a security
    // exploit — but a production deploy that silently kept the localhost
    // default would fail in a confusing way ("works from my machine,
    // nothing else can reach it") without ever being told why.
    if (app.Environment.IsProduction() && corsOrigins.All(o => o.Contains("localhost")))
        logger.LogWarning(
            "Cors:AllowedOrigins is still only localhost while running in Production ({Origins}). " +
            "Set it to your real frontend domain(s) or the dashboard will be unable to reach this API.",
            string.Join(", ", corsOrigins));
}

app.UseBmsExceptionHandling();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "BMS API v1"));
}

app.UseCors("Dashboard");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<TelemetryHub>("/hubs/realtime");

// Health probes are for docker-compose/orchestrators, not dashboard users —
// they must not require a JWT (the fallback policy above requires one on
// every other endpoint).
app.MapHealthChecks("/health").AllowAnonymous();
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => true }).AllowAnonymous();

// Same reasoning as the health probes: a scraper has no JWT. Keep this port
// on the private network — it is not for dashboard users.
if (metricsEnabled)
    app.MapPrometheusScrapingEndpoint().AllowAnonymous();

app.Run();

public partial class Program; // exposed for WebApplicationFactory in integration tests
