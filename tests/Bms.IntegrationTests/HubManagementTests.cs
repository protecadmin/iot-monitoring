using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Realtime;
using Bms.Application.Abstractions.Transports;
using Bms.Application.Common;
using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Infrastructure.Persistence;
using Bms.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Bms.Ingestion.Workers;
using Microsoft.Extensions.DependencyInjection;
using Bms.Api.Common;
using Bms.Api.Controllers;
using Bms.Api.Dtos;
using Bms.Protocols.Configuration;
using Bms.Protocols.Transports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Bms.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public class HubManagementTests(BmsApiFactory factory)
{
    private const string HubCode = "aabbccddeeff";

    [Fact]
    public async Task Controller_blocks_unverified_apply_stale_versions_and_unsupported_operations_before_publish()
    {
        using var scope = factory.Services.CreateScope();
        var shared = scope.ServiceProvider.GetRequiredService<BmsDbContext>();
        await using var db = new BmsDbContext(new DbContextOptionsBuilder<BmsDbContext>().UseNpgsql(shared.Database.GetConnectionString()).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        IHubRepository repo = new HubRepository(db);
        var hub = await repo.AddAsync(new Hub { HubId = HubCode, Name = "Controller test", Status = HubStatus.Online, LastSeenAt = DateTimeOffset.UtcNow,
            LastHeartbeatAt = DateTimeOffset.UtcNow, LastStatusJson = JsonSerializer.Serialize(new { v = 2, hub = HubCode, config_version = 1, time_utc = DateTimeOffset.UtcNow }) }, default);
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var controller = new HubsController(repo, scope.ServiceProvider.GetRequiredService<IAuditLogRepository>(),
            scope.ServiceProvider.GetRequiredService<MqttTransportAdapter>(), config, Options.Create(new MqttOptions()));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "SuperAdmin")], "test")) } };
        var request = new HubCommandRequest("display.set", new() { ["welcome_seconds"] = 0, ["rotation_seconds"] = 15 }, 1);
        var disabled = await Assert.ThrowsAsync<ApiException>(() => controller.Command(hub.Id, null, request, default));
        Assert.Equal("HUB_APPLY_DISABLED", disabled.ErrorCode);
        var legacy = await Assert.ThrowsAsync<ApiException>(() => controller.SetDesiredConfig(hub.Id, null, new HubDesiredConfigRequest(RotationSeconds: 15, WelcomeSeconds: 0), default));
        Assert.Equal("HUB_APPLY_DISABLED", legacy.ErrorCode);
        config["HubManagement:ApplyEnabledHubIds:0"] = HubCode;
        var stale = await Assert.ThrowsAsync<ApiException>(() => controller.Command(hub.Id, null, request with { IfVersion = 0 }, default));
        Assert.Equal("HUB_VERSION_CONFLICT", stale.ErrorCode);
        var unsupported = await Assert.ThrowsAsync<ApiException>(() => controller.Command(hub.Id, null, new HubCommandRequest("ota.install"), default));
        Assert.Equal("HUB_INVALID_COMMAND", unsupported.ErrorCode);
        Assert.Empty(await repo.GetCommandsAsync(hub.Id, default));
        var historicalId = $"cmd-{Guid.NewGuid():N}";
        var historicalRequest = request with { IfVersion = 0, RequestId = historicalId };
        var envelope = HubProtocol.Build("display.set", JsonSerializer.SerializeToNode(request.Arguments)!.AsObject(), 0, historicalId);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(envelope)));
        await repo.RecordCommandAsync(hub.Id, null, null, new(historicalId, "display.set", "applied", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(2), 0, envelope["data"]!.DeepClone().AsObject(), PayloadHash: hash), default);
        var duplicate = Assert.IsType<AcceptedResult>(await controller.Command(hub.Id, null, historicalRequest, default));
        Assert.True(JsonSerializer.SerializeToElement(duplicate.Value).GetProperty("duplicate").GetBoolean());
        Assert.Single(await repo.GetCommandsAsync(hub.Id, default));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Journal_correlates_id_and_operation_and_requires_matching_snapshot_before_applied()
    {
        using var scope = factory.Services.CreateScope();
        var shared = scope.ServiceProvider.GetRequiredService<BmsDbContext>();
        await using var db = new BmsDbContext(new DbContextOptionsBuilder<BmsDbContext>().UseNpgsql(shared.Database.GetConnectionString()).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        IHubRepository repo = new HubRepository(db);
        var hub = await repo.AddAsync(new Hub { HubId = HubCode, Name = "Protocol test" }, default);
        var id = $"cmd-{Guid.NewGuid():N}"; var now = DateTimeOffset.UtcNow;
        var request = new HubCommandReceipt(id, "display.set", "pending", now, now.AddMinutes(2), 1,
            new JsonObject { ["welcome_seconds"] = 0, ["rotation_seconds"] = 15 });
        Assert.True(await repo.RecordCommandAsync(hub.Id, null, null, request, default));
        Assert.False(await repo.RecordCommandAsync(hub.Id, null, null, request, default));
        async Task Ack(string requestId, string op, string state) => await repo.ProcessCommandReplyAsync(HubCode, "ack",
            JsonSerializer.Serialize(new { v = 2, hub = HubCode, id = requestId, op, state, config_version = 2 }), now, default);
        await Ack("unrelated", "display.set", "applied");
        await Ack(id, "get", "succeeded");
        Assert.Equal("pending", (await repo.GetCommandsAsync(hub.Id, default)).Single().State);
        await Ack(id, "display.set", "applied");
        Assert.Equal("awaiting_snapshot", (await repo.GetCommandsAsync(hub.Id, default)).Single().State);
        await repo.ProcessCommandReplyAsync(HubCode, "snapshot", """{"v":2,"hub":"aabbccddeeff","config_version":2,"tv":{"welcome_seconds":7,"rotation_seconds":15}}""", now, default);
        Assert.Equal("awaiting_snapshot", (await repo.GetCommandsAsync(hub.Id, default)).Single().State);
        await repo.ProcessCommandReplyAsync(HubCode, "snapshot", """{"v":2,"hub":"aabbccddeeff","config_version":2,"tv":{"welcome_seconds":0,"rotation_seconds":15}}""", now, default);
        await Ack(id, "display.set", "received");
        Assert.Equal("applied", (await repo.GetCommandsAsync(hub.Id, default)).Single().State);
        var monitor = await repo.GetMonitorAsync(hub.Id, now.AddMinutes(-1), default);
        Assert.Equal(1, monitor.CommandsToday);
        Assert.Equal(100, monitor.SuccessRate);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Ingestion_preserves_large_snapshot_and_rejects_outbound_echo_and_retained_offline_heartbeat()
    {
        using var scope = factory.Services.CreateScope();
        var shared = scope.ServiceProvider.GetRequiredService<BmsDbContext>();
        await using var db = new BmsDbContext(new DbContextOptionsBuilder<BmsDbContext>().UseNpgsql(shared.Database.GetConnectionString()).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        IHubRepository repo = new HubRepository(db);
        var hub = await repo.AddAsync(new Hub { HubId = HubCode, Name = "Ingestion test" }, default);
        var notifier = DispatchProxy.Create<IRealtimeNotifier, GatewayStatusPayloadTests.RecordingProxy>();
        HubMessageUpdate? received = null;
        ((GatewayStatusPayloadTests.RecordingProxy)(object)notifier).OnCall = (m, args) => { if (m.Name == nameof(IRealtimeNotifier.HubMessageReceivedAsync)) received = (HubMessageUpdate)args[0]!; };
        using var services = new ServiceCollection().AddSingleton(repo).AddSingleton(notifier).BuildServiceProvider();
        var handler = typeof(MqttIngestionWorker).GetMethod("HandleHubMessageAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        async Task Send(string suffix, object payload, bool retained = false)
        {
            var message = new TransportMessage(TransportType.Mqtt, $"protec/hubs/{HubCode}/{suffix}", JsonSerializer.SerializeToUtf8Bytes(payload), DateTimeOffset.UtcNow) { IsRetained = retained };
            await (Task)handler.Invoke(null, [services, HubCode, message, CancellationToken.None])!;
        }
        await Send("config", new { v = 2, hub = HubCode, op = "get" });
        Assert.Null(hub.LastSeenAt);
        await Send("snapshot", new { v = 2, hub = HubCode, firmware = "0.3.16", config_version = 4, tv = new { welcome_seconds = 0, rotation_seconds = 15, mode = "overview" }, padding = new string('x', 4000), password = "must-redact" });
        Assert.NotNull(received);
        Assert.True(Encoding.UTF8.GetByteCount(received.Payload) > 4000);
        Assert.DoesNotContain("must-redact", received.Payload);
        Assert.Equal(4, hub.ConfigRevision);
        Assert.Equal(0, hub.WelcomeSeconds);
        Assert.Equal("0.3.16", hub.AppVersion);
        using var json = JsonDocument.Parse(hub.LastStatusJson!);
        Assert.Equal(4000, json.RootElement.GetProperty("padding").GetString()!.Length);
        await Send("heartbeat", new { v = 2, hub = HubCode, online = false, time_utc = DateTimeOffset.UtcNow });
        Assert.Equal(HubStatus.Offline, hub.Status);
        await Send("heartbeat", new { v = 2, hub = HubCode, online = true, time_utc = DateTimeOffset.UtcNow.AddHours(-1) }, true);
        Assert.Equal(HubStatus.Offline, hub.Status);
        var monitor = await repo.GetMonitorAsync(hub.Id, DateTimeOffset.UtcNow.Date, default);
        Assert.Equal(2, monitor.Messages.Count);
        Assert.True(monitor.Messages.Single(m => m.Topic.EndsWith("/snapshot")).Bytes > 4000);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Expired_requests_report_timeout_and_rejections_keep_device_error_details()
    {
        using var scope = factory.Services.CreateScope();
        var shared = scope.ServiceProvider.GetRequiredService<BmsDbContext>();
        await using var db = new BmsDbContext(new DbContextOptionsBuilder<BmsDbContext>().UseNpgsql(shared.Database.GetConnectionString()).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        IHubRepository repo = new HubRepository(db);
        var hub = await repo.AddAsync(new Hub { HubId = HubCode, Name = "Timeout test" }, default);
        var id = $"cmd-{Guid.NewGuid():N}";
        await repo.RecordCommandAsync(hub.Id, null, null, new(id, "get", "pending", DateTimeOffset.UtcNow.AddMinutes(-3), DateTimeOffset.UtcNow.AddMinutes(-1), null, []), default);
        Assert.Equal("timed_out", (await repo.GetCommandsAsync(hub.Id, default)).Single().State);
        await repo.MarkPublishFailureAsync(hub.Id, id, default);
        var publishFailure = (await repo.GetCommandsAsync(hub.Id, default)).Single();
        Assert.Equal("publish_failed", publishFailure.State);
        Assert.Null(publishFailure.AcknowledgedAt);
        await repo.ProcessCommandReplyAsync(HubCode, "ack", JsonSerializer.Serialize(new { v = 2, hub = HubCode, id, op = "get", state = "rejected", code = "version_conflict", message = "Read a fresh snapshot" }), DateTimeOffset.UtcNow, default);
        var result = (await repo.GetCommandsAsync(hub.Id, default)).Single();
        Assert.Equal("rejected", result.State);
        Assert.Equal("version_conflict", result.Code);
        await transaction.RollbackAsync();
    }
}
