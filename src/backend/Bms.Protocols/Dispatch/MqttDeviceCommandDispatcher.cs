using System.Text.Json;
using Bms.Application.Abstractions.Ota;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Security;
using Bms.Domain.Entities;
using Bms.Protocols.Transports;
using Microsoft.Extensions.Logging;

namespace Bms.Protocols.Dispatch;

/// <summary>
/// Publishes commands/config/firmware pushes to a device's MQTT topics —
/// the Backend -> Device direction of docs/mqtt/topic-design.md. Uses the
/// same tenant/site/device codes a device subscribes with, resolved via
/// the device registry (the dispatcher only ever sees Guid keys).
/// </summary>
public sealed class MqttDeviceCommandDispatcher : IDeviceCommandDispatcher
{
    private readonly MqttTransportAdapter _mqtt;
    private readonly ISiteRepository _sites;
    private readonly ITenantRepository _tenants;
    private readonly IFirmwareDownloadLinks _downloadLinks;
    private readonly ILogger<MqttDeviceCommandDispatcher> _logger;

    public MqttDeviceCommandDispatcher(
        MqttTransportAdapter mqtt, ISiteRepository sites, ITenantRepository tenants,
        IFirmwareDownloadLinks downloadLinks, ILogger<MqttDeviceCommandDispatcher> logger)
    {
        _mqtt = mqtt;
        _sites = sites;
        _tenants = tenants;
        _downloadLinks = downloadLinks;
        _logger = logger;
    }

    public async Task SendCommandAsync(Device device, DeviceCommand command, CancellationToken ct)
    {
        var topic = await BuildTopicAsync(device, "commands", ct);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            commandId = command.Id,
            type = command.Type.ToString(),
            payload = command.PayloadJson
        });

        await PublishAsync(topic, payload, ct);
    }

    public async Task SendConfigurationAsync(Device device, DeviceConfiguration configuration, CancellationToken ct)
    {
        var topic = await BuildTopicAsync(device, "config", ct);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = configuration.Version,
            reportingInterval = configuration.ReportingIntervalSeconds,
            heartbeatInterval = configuration.HeartbeatIntervalSeconds,
            temperatureThreshold = configuration.TemperatureThreshold
        });

        await PublishAsync(topic, payload, ct);
    }

    public async Task SendFirmwareDeploymentAsync(Device device, Firmware firmware, FirmwareDeployment deployment, CancellationToken ct)
    {
        var topic = await BuildTopicAsync(device, "firmware", ct);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            deploymentId = deployment.Id,
            firmwareId = firmware.Id,
            version = firmware.Version,
            // MQTT carries the instruction; the binary itself is fetched over
            // HTTPS from this signed, expiring link, then verified against
            // sha256Checksum before installing.
            downloadUrl = _downloadLinks.Create(firmware.Id, deployment.Id),
            fileName = firmware.FileName,
            sha256Checksum = firmware.Sha256Checksum,
            sizeBytes = firmware.SizeBytes
        });

        await PublishAsync(topic, payload, ct);
    }

    private async Task<string> BuildTopicAsync(Device device, string messageType, CancellationToken ct)
    {
        var site = await _sites.GetByIdAsync(device.TenantId, device.SiteId, ct)
                   ?? throw new InvalidOperationException($"Site {device.SiteId} not found for device {device.Id}");
        var tenant = await _tenants.GetByIdAsync(device.TenantId, ct)
                     ?? throw new InvalidOperationException($"Tenant {device.TenantId} not found for device {device.Id}");

        return $"iot/{tenant.Slug}/{site.Code}/{device.DeviceCode}/{messageType}";
    }

    private async Task PublishAsync(string topic, byte[] payload, CancellationToken ct)
    {
        if (!_mqtt.IsConnected)
        {
            _logger.LogWarning("Cannot publish to {Topic}: MQTT not connected", topic);
            throw new InvalidOperationException("MQTT broker is not connected");
        }

        await _mqtt.SendAsync(topic, payload, ct);
    }
}
