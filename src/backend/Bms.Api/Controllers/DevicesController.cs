using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Common;
using Bms.DeviceManagement.Configuration;
using Bms.Domain;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Bms.Api.Controllers;

[ApiController]
[Route("api/devices")]
[Authorize]
public sealed class DevicesController(
    IDeviceRepository devices,
    ITenantRepository tenants,
    ISiteRepository sites,
    IDeviceTypeRepository deviceTypes,
    IConnectorRepository connectors,
    IAuditLogRepository auditLog,
    IOptions<DeviceLifecycleOptions> lifecycle) : BmsControllerBase
{
    /// <summary>
    /// The Online/Offline timing rules. The alert values are the defaults an
    /// alert rule falls back to when it has no threshold of its own.
    /// </summary>
    [HttpGet("lifecycle-settings")]
    public ActionResult<DeviceLifecycleSettingsDto> LifecycleSettings()
    {
        var o = lifecycle.Value;
        return Ok(new DeviceLifecycleSettingsDto(o.OfflineAfterSeconds, o.SweepIntervalSeconds, o.NoTelemetryAlertAfterSeconds, o.DeviceOfflineAlertAfterSeconds));
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<DeviceDto>>> List(
        [FromQuery] Guid? tenantId, [FromQuery] Guid? siteId, [FromQuery] Guid? deviceTypeId,
        [FromQuery] TransportType? transport, [FromQuery] ProtocolType? protocol, [FromQuery] DeviceStatus? status,
        [FromQuery] string? search, [FromQuery] string? sortBy, [FromQuery] bool sortDescending = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var effectiveTenantId = EffectiveTenantFilter(tenantId);

        var filter = new DeviceFilter
        {
            TenantId = effectiveTenantId,
            SiteId = siteId,
            DeviceTypeId = deviceTypeId,
            Transport = transport,
            Protocol = protocol,
            Status = status,
            Search = search,
            SortBy = sortBy,
            SortDescending = sortDescending,
            Page = page,
            PageSize = pageSize
        };

        var result = await devices.ListAsync(filter, ct);
        return Ok(new PagedResult<DeviceDto>
        {
            Items = result.Items.Select(MapForCaller).ToList(),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DeviceDto>> GetById(Guid id, CancellationToken ct)
    {
        var device = await devices.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        RequireTenantAccess(device.TenantId);
        return Ok(MapForCaller(device));
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<DeviceDto>> Create([FromQuery] Guid tenantId, CreateDeviceRequest request, CancellationToken ct)
    {
        // A SuperAdmin names the company explicitly — without it the device
        // would belong to no company, and its MQTT topic would match no tenant.
        if (tenantId == Guid.Empty)
            throw ApiException.BadRequest("tenantId is required", "TENANT_REQUIRED");
        RequireTenantAccess(tenantId);
        if (await sites.GetByIdAsync(tenantId, request.SiteId, ct) is null)
            throw ApiException.BadRequest("The site doesn't belong to this company", "SITE_NOT_IN_TENANT");

        // A connector device's code is the connector's prefix + the ID the
        // device puts in its messages — the connector finds it by that ID.
        var deviceCode = request.DeviceCode;
        string? sensorId = null;
        Guid? connectorId = null;
        if (request.Connection == DeviceConnection.Connector)
        {
            var connector = request.ConnectorId is { } cid ? await connectors.GetAsync(cid, ct) : null;
            if (connector is null)
                throw ApiException.BadRequest("Pick the connector this device sends to", "CONNECTOR_REQUIRED");
            if (!connector.IsEnabled)
                throw ApiException.BadRequest($"Connector \"{connector.Name}\" is disabled", "CONNECTOR_DISABLED");
            sensorId = request.SensorId?.Trim();
            if (string.IsNullOrEmpty(sensorId) || !Bms.Connectors.Core.MessageDecoder.IsSensorId(sensorId))
                throw ApiException.BadRequest("Sensor ID: the ID the device sends — up to 64 letters, digits, '-', '_' or '.'", "SENSOR_ID_INVALID");

            // A connector that fills none of the type's parameters would store nothing.
            var type = await deviceTypes.GetByIdAsync(request.DeviceTypeId, ct) ?? throw ApiException.BadRequest("Unknown device type", "DEVICE_TYPE_NOT_FOUND");
            var typeKeys = type.Parameters.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var mapped = connector.Mappings.Where(m => m.Target == Bms.Domain.Connectors.ConnectorTarget.Parameter && m.ParameterKey is not null).Select(m => m.ParameterKey!).ToList();
            if (!mapped.Any(typeKeys.Contains))
                throw ApiException.BadRequest(
                    $"Connector \"{connector.Name}\" sends {string.Join(", ", mapped)}; {type.Name} has none of these, so nothing would be stored. Pick another connector or device type.",
                    "CONNECTOR_TYPE_MISMATCH");

            deviceCode = connector.DeviceCodePrefix + sensorId;
            connectorId = connector.Id;
        }
        if (string.IsNullOrWhiteSpace(deviceCode))
            throw ApiException.BadRequest("Device code is required", "DEVICE_CODE_REQUIRED");

        if (await devices.GetByDeviceCodeAsync(deviceCode, ct) is not null)
            throw ApiException.Conflict(sensorId is null ? "A device with this code already exists" : $"Sensor {sensorId} is already registered on this connector", "DEVICE_CODE_TAKEN");

        var mqttTopic = await ValidateMqttTopicAsync(request.MqttTelemetryTopic, request.Connection, null, tenantId, request.SiteId, deviceCode, ct);
        var device = new Device
        {
            TenantId = tenantId,
            SiteId = request.SiteId,
            DeviceTypeId = request.DeviceTypeId,
            GatewayId = request.GatewayId,
            DeviceCode = deviceCode,
            MqttTelemetryTopic = mqttTopic,
            Connection = request.Connection,
            ConnectorId = connectorId,
            SensorId = sensorId,
            InstalledOn = request.InstalledOn,
            MountingLocation = string.IsNullOrWhiteSpace(request.MountingLocation) ? null : request.MountingLocation.Trim(),
            Name = request.Name,
            SerialNumber = request.SerialNumber,
            Manufacturer = request.Manufacturer,
            Model = request.Model,
            // Every device reports through our own gateway: JSON over MQTT.
            Transport = TransportType.Mqtt,
            Protocol = ProtocolType.Mqtt,
            PayloadFormat = PayloadFormat.Json,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Status = DeviceStatus.Registered
        };

        await devices.AddAsync(device, ct);
        // Registered now: it is no longer an "unknown device" on its connector.
        if (connectorId is { } registeredOn)
            await connectors.DeleteUnknownDeviceAsync(registeredOn, sensorId!, ct);
        await auditLog.AddAsync(new AuditLog
        {
            TenantId = tenantId,
            UserId = CurrentUserId,
            Action = "Device.Create",
            EntityType = nameof(Device),
            EntityId = device.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { device.DeviceCode, device.Name })
        }, ct);
        var created = await devices.GetByIdAsync(tenantId, device.Id, ct) ?? device;
        return CreatedAtAction(nameof(GetById), new { id = device.Id }, Map(created));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<DeviceDto>> Update(Guid id, UpdateDeviceRequest request, CancellationToken ct)
    {
        var device = await devices.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        RequireTenantAccess(device.TenantId);

        device.MqttTelemetryTopic = await ValidateMqttTopicAsync(request.MqttTelemetryTopic, device.Connection, device.Id, device.TenantId, device.SiteId, device.DeviceCode, ct);
        device.Name = request.Name;
        device.SerialNumber = request.SerialNumber;
        device.Manufacturer = request.Manufacturer;
        device.Model = request.Model;
        device.Latitude = request.Latitude;
        device.Longitude = request.Longitude;
        device.GatewayId = request.GatewayId;
        device.InstalledOn = request.InstalledOn;
        device.MountingLocation = string.IsNullOrWhiteSpace(request.MountingLocation) ? null : request.MountingLocation.Trim();

        await devices.UpdateAsync(device, ct);
        await auditLog.AddAsync(new AuditLog
        {
            TenantId = device.TenantId,
            UserId = CurrentUserId,
            Action = "Device.Update",
            EntityType = nameof(Device),
            EntityId = device.Id.ToString()
        }, ct);
        // Re-fetch rather than reuse the in-memory instance — if GatewayId
        // just changed, the already-tracked Gateway nav property would
        // otherwise still point at the old one until reloaded.
        var updated = await devices.GetByIdAsync(device.TenantId, device.Id, ct) ?? device;
        return Ok(Map(updated));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var device = await devices.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        RequireTenantAccess(device.TenantId);
        await devices.DeleteAsync(device.TenantId, id, ct);
        await auditLog.AddAsync(new AuditLog
        {
            TenantId = device.TenantId,
            UserId = CurrentUserId,
            Action = "Device.Delete",
            EntityType = nameof(Device),
            EntityId = device.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { device.DeviceCode, device.Name })
        }, ct);
        return NoContent();
    }

    /// <summary>
    /// Chooses how the digital twin is drawn: template (picture), the sensor's
    /// mount point on it, and an optional equipment name. All null resets to
    /// the default for the device type. The template and mount point ids are
    /// the dashboard's; the server only checks their shape.
    /// </summary>
    /// <summary>Turns a device's alerts off (e.g. maintenance) or back on. Readings keep being stored either way.</summary>
    [HttpPut("{id:guid}/alerts")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<DeviceDto>> SetAlerts(Guid id, SetDeviceAlertsRequest request, CancellationToken ct)
    {
        var device = await devices.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        RequireTenantAccess(device.TenantId);

        if (device.AlertsEnabled != request.Enabled)
        {
            device.AlertsEnabled = request.Enabled;
            await devices.UpdateAsync(device, ct);
            await auditLog.AddAsync(new AuditLog
            {
                TenantId = device.TenantId,
                UserId = CurrentUserId,
                Action = request.Enabled ? "Device.AlertsOn" : "Device.AlertsOff",
                EntityType = nameof(Device),
                EntityId = device.Id.ToString(),
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { device.DeviceCode, device.Name })
            }, ct);
        }

        return Ok(Map(device));
    }

    [HttpPut("{id:guid}/digital-twin")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<DeviceDto>> SetDigitalTwin(Guid id, DigitalTwinSettingsDto request, CancellationToken ct)
    {
        var device = await devices.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        RequireTenantAccess(device.TenantId);

        device.TwinTemplate = TwinId(request.Template, "template");
        device.TwinMountPoint = TwinId(request.MountPoint, "sensor position");
        var name = string.IsNullOrWhiteSpace(request.AssetName) ? null : request.AssetName.Trim();
        if (name is { Length: > 80 })
            throw ApiException.BadRequest("Equipment name must be 80 characters or fewer", "TWIN_ASSET_NAME_TOO_LONG");
        device.TwinAssetName = name;

        await devices.UpdateAsync(device, ct);
        await auditLog.AddAsync(new AuditLog
        {
            TenantId = device.TenantId,
            UserId = CurrentUserId,
            Action = "Device.SetDigitalTwin",
            EntityType = nameof(Device),
            EntityId = device.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { device.DeviceCode, device.TwinTemplate, device.TwinMountPoint, device.TwinAssetName })
        }, ct);

        var updated = await devices.GetByIdAsync(device.TenantId, device.Id, ct) ?? device;
        return Ok(Map(updated));
    }

    /// <summary>
    /// Unit corrections for an MQTT-direct device that sends a wrong unit,
    /// applied by ingestion before the value is stored. Readings already
    /// stored keep the value they were stored with.
    /// </summary>
    [HttpPut("{id:guid}/value-conversions")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<DeviceDto>> SetValueConversions(Guid id, SetDeviceValueConversionsRequest request, CancellationToken ct)
    {
        var device = await devices.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        RequireTenantAccess(device.TenantId);
        if (device.Connection != DeviceConnection.MqttDirect)
            throw ApiException.BadRequest("This device sends through a connector — convert its values in the connector's mapping (Offset and Scale)", "VALUE_CONVERSION_CONNECTOR_DEVICE");

        var type = await deviceTypes.GetByIdAsync(device.DeviceTypeId, ct);
        var parameters = type?.Parameters ?? [];
        var conversions = new List<DeviceValueConversion>();
        foreach (var c in request.Conversions ?? [])
        {
            var parameter = parameters.FirstOrDefault(p => string.Equals(p.Key, c.ParameterKey?.Trim(), StringComparison.OrdinalIgnoreCase))
                            ?? throw ApiException.BadRequest($"\"{c.ParameterKey}\" isn't a parameter of {type?.Name ?? "this device type"}", "VALUE_CONVERSION_PARAMETER_UNKNOWN");
            if (conversions.Any(x => x.ParameterKey == parameter.Key))
                throw ApiException.BadRequest($"\"{parameter.Key}\" has more than one conversion", "VALUE_CONVERSION_DUPLICATE");
            if (!double.IsFinite(c.Offset) || !double.IsFinite(c.Scale) || c.Scale == 0)
                throw ApiException.BadRequest($"\"{parameter.Key}\": Offset must be a number and Scale a number other than 0", "VALUE_CONVERSION_INVALID");
            conversions.Add(new DeviceValueConversion { ParameterKey = parameter.Key, Offset = c.Offset, Scale = c.Scale });
        }

        var before = device.ValueConversions.Select(c => new { c.ParameterKey, c.Offset, c.Scale }).ToList();
        device.ValueConversions = conversions;
        await devices.UpdateAsync(device, ct);
        // It changes what is stored from now on, so who changed it and from what is kept.
        await auditLog.AddAsync(new AuditLog
        {
            TenantId = device.TenantId,
            UserId = CurrentUserId,
            Action = "Device.SetValueConversions",
            EntityType = nameof(Device),
            EntityId = device.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                device.DeviceCode,
                Before = before,
                After = conversions.Select(c => new { c.ParameterKey, c.Offset, c.Scale })
            })
        }, ct);

        var updated = await devices.GetByIdAsync(device.TenantId, device.Id, ct) ?? device;
        return Ok(Map(updated));
    }

    private static string? TwinId(string? value, string what)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var id = value.Trim();
        if (id.Length > 40 || !id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            throw ApiException.BadRequest($"Invalid {what} \"{id}\"", "TWIN_ID_INVALID");
        return id;
    }

    internal static DeviceDto Map(Device d) => new(
        d.Id, d.TenantId, d.SiteId, d.DeviceTypeId,
        d.DeviceCode, d.Name, d.SerialNumber, d.Manufacturer, d.Model,
        d.Transport.ToString(), d.Protocol.ToString(), d.PayloadFormat.ToString(),
        d.FirmwareVersion, d.HardwareVersion, d.Status.ToString(),
        d.Latitude, d.Longitude, d.LastSeenAt, d.LastTelemetryAt, d.LastHeartbeatAt,
        d.GatewayId, d.Gateway?.Name, d.CreatedAt,
        d.TwinTemplate is null && d.TwinMountPoint is null && d.TwinAssetName is null
            ? null
            : new DigitalTwinSettingsDto(d.TwinTemplate, d.TwinMountPoint, d.TwinAssetName),
        d.AlertsEnabled,
        d.TimeSource.ToString(),
        d.Connection.ToString(),
        d.ConnectorId,
        d.SensorId,
        d.InstalledOn,
        d.MountingLocation,
        d.ValueConversions.Select(c => new DeviceValueConversionDto(c.ParameterKey, c.Offset, c.Scale)).ToList(),
        d.MqttTelemetryTopic,
        d.Connection == DeviceConnection.MqttDirect ? d.MqttTelemetryTopic ?? MqttTelemetryTopicRules.DefaultFor(d.DeviceCode) : null);

    private DeviceDto MapForCaller(Device d) => IsSuperAdmin ? Map(d) : Map(d) with
    {
        GatewayId = null, GatewayName = null, ConnectorId = null,
        MqttTelemetryTopic = null, EffectiveMqttTelemetryTopic = null
    };

    private async Task<string?> ValidateMqttTopicAsync(string? topic, DeviceConnection connection, Guid? id,
        Guid tenantId, Guid siteId, string deviceCode, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(topic)) topic = null;
        if (connection != DeviceConnection.MqttDirect)
        {
            if (topic is not null) throw ApiException.BadRequest("Custom MQTT topics apply to MQTT-direct devices only", "MQTT_TOPIC_CONNECTION");
            return null;
        }
        var effectiveTopic = topic ?? MqttTelemetryTopicRules.DefaultFor(deviceCode);
        var validationError = topic is null ? MqttTelemetryTopicRules.ValidateGatewayId(deviceCode) : MqttTelemetryTopicRules.Validate(topic);
        if (validationError is { } error)
            throw ApiException.BadRequest(error, "MQTT_TOPIC_INVALID");
        var existing = await devices.GetByMqttTopicAsync(effectiveTopic, ct);
        if (existing is not null && existing.Id != id)
            throw ApiException.Conflict("This MQTT topic is already assigned to a device", "MQTT_TOPIC_TAKEN");
        // Existing iot/... addresses retain their company/site/device ownership.
        if (topic is not null && Bms.Protocols.Protocols.MqttTopic.TryParse(topic, out var canonical) && canonical is not null)
        {
            var tenant = await tenants.GetByIdAsync(tenantId, ct);
            var site = await sites.GetByIdAsync(tenantId, siteId, ct);
            if (canonical.TenantSlug != tenant?.Slug || canonical.SiteCode != site?.Code || canonical.DeviceCode != deviceCode)
                throw ApiException.BadRequest("An existing iot/ address must identify this company, site and device", "MQTT_TOPIC_OWNER_MISMATCH");
        }
        return topic;
    }
}
