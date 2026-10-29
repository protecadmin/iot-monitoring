using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Protocols;
using Bms.Application.Abstractions.Transports;
using Bms.Domain;
using Bms.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Bms.Protocols.Protocols;

public sealed class MqttProtocolHandler : IProtocolHandler
{
    private readonly IDeviceRepository _devices;
    private readonly ILogger<MqttProtocolHandler> _logger;

    public ProtocolType Protocol => ProtocolType.Mqtt;

    public MqttProtocolHandler(IDeviceRepository devices, ILogger<MqttProtocolHandler> logger)
    {
        _devices = devices;
        _logger = logger;
    }

    public async Task<RawDeviceMessage?> TryBuildRawMessageAsync(TransportMessage message, CancellationToken cancellationToken)
    {
        if (!MqttTopic.TryParse(message.Address, out var topic) || topic is null)
        {
            _logger.LogWarning("Could not parse MQTT topic {Topic}", message.Address);
            return null;
        }

        var device = await _devices.GetByCodesAsync(topic.TenantSlug, topic.SiteCode, topic.DeviceCode, cancellationToken);
        if (device is null)
        {
            _logger.LogWarning("Unknown device for MQTT topic {Topic}", message.Address);
            return null;
        }

        return new RawDeviceMessage
        {
            TenantId = device.TenantId,
            SiteId = device.SiteId,
            DeviceId = device.Id,
            Transport = TransportType.Mqtt,
            Protocol = ProtocolType.Mqtt,
            Topic = message.Address,
            PayloadFormat = device.PayloadFormat,
            RawPayload = message.Payload,
            ReceivedAt = message.ReceivedAt
        };
    }
}
