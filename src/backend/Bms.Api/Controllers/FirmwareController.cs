using System.Text.RegularExpressions;
using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Ota;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Realtime;
using Bms.Application.Abstractions.Security;
using Bms.Application.Ota;
using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Infrastructure.Ota;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Bms.Api.Controllers;

/// <summary>
/// Firmware over the air. A release is a binary for one device type; a
/// deployment sends it to one device. The instruction travels over MQTT
/// (…/firmware), the device downloads the binary from a signed link on this
/// controller, verifies its SHA-256, installs, and reports each stage on
/// …/events (see MqttIngestionWorker.HandleEventAsync).
/// </summary>
[ApiController]
[Route("api/firmware")]
[Authorize]
public sealed partial class FirmwareController(
    IFirmwareRepository firmwares,
    IFirmwareDeploymentRepository deployments,
    IDeviceRepository devices,
    IDeviceTypeRepository deviceTypes,
    IDeviceCommandDispatcher dispatcher,
    IRealtimeNotifier notifier,
    IFirmwareStorage storage,
    IFirmwareDownloadLinks downloadLinks,
    IOptions<FirmwareOptions> options,
    IAuditLogRepository auditLog,
    ILogger<FirmwareController> logger) : BmsControllerBase
{
    [GeneratedRegex(@"^[0-9A-Za-z][0-9A-Za-z.\-+_]{0,31}$")]
    private static partial Regex VersionPattern();

    [HttpGet]
    [Authorize(Roles = "SuperAdmin")] // not Customer: integration detail
    public async Task<ActionResult<IReadOnlyList<FirmwareDto>>> List(CancellationToken ct)
    {
        var all = await firmwares.ListAsync(ct);
        return Ok(all.Select(Map).ToList());
    }

    /// <summary>Uploads a release: the binary is stored and its real SHA-256 and size recorded.</summary>
    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    [Consumes("multipart/form-data")]
    // The configured MaxFileSizeMb is enforced while streaming (see
    // LocalFirmwareStorage); these only lift the framework's smaller defaults.
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<ActionResult<FirmwareDto>> Create([FromForm] CreateFirmwareForm form, CancellationToken ct)
    {
        var deviceType = await deviceTypes.GetByIdAsync(form.DeviceTypeId, ct)
                         ?? throw ApiException.BadRequest("Choose a device type", "DEVICE_TYPE_NOT_FOUND");

        var version = form.Version?.Trim() ?? string.Empty;
        if (!VersionPattern().IsMatch(version))
            throw ApiException.BadRequest("Version must be 1–32 characters: letters, digits, and . - + _ (e.g. 1.2.0)", "FIRMWARE_VERSION_INVALID");
        if (await firmwares.VersionExistsAsync(deviceType.Id, version, ct))
            throw ApiException.Conflict($"Version {version} already exists for {deviceType.Name}", "FIRMWARE_VERSION_EXISTS");

        if (form.File is null || form.File.Length == 0)
            throw ApiException.BadRequest("Choose the firmware file to upload", "FIRMWARE_FILE_REQUIRED");

        var firmware = new Firmware
        {
            DeviceTypeId = deviceType.Id,
            Version = version,
            FileName = SafeFileName(form.File.FileName, version),
            ReleaseNotes = string.IsNullOrWhiteSpace(form.ReleaseNotes) ? null : form.ReleaseNotes.Trim()
        };

        StoredFirmwareFile stored;
        try
        {
            await using var upload = form.File.OpenReadStream();
            stored = await storage.SaveAsync(firmware.Id, upload, options.Value.MaxFileSizeMb * 1024L * 1024L, ct);
        }
        catch (FirmwareTooLargeException ex)
        {
            throw ApiException.BadRequest(ex.Message, "FIRMWARE_FILE_TOO_LARGE");
        }

        firmware.Sha256Checksum = stored.Sha256Checksum;
        firmware.SizeBytes = stored.SizeBytes;
        try
        {
            await firmwares.AddAsync(firmware, ct);
        }
        catch
        {
            storage.Delete(firmware.Id); // no orphaned binary without a release
            throw;
        }

        await auditLog.AddAsync(new AuditLog
        {
            UserId = CurrentUserId,
            Action = "Firmware.Create",
            EntityType = nameof(Firmware),
            EntityId = firmware.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { DeviceType = deviceType.Name, firmware.Version, firmware.FileName, firmware.SizeBytes, firmware.Sha256Checksum })
        }, ct);

        return Ok(Map(firmware));
    }

    /// <summary>Deletes a release, its binary and its deployment history. Refused while an update is still in progress.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var firmware = await firmwares.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Firmware not found", "FIRMWARE_NOT_FOUND");
        if (await deployments.AnyActiveForFirmwareAsync(id, ct))
            throw ApiException.Conflict("This release is still being installed on some devices — wait until they finish, fail or time out", "FIRMWARE_IN_USE");

        await firmwares.DeleteAsync(firmware, ct);
        storage.Delete(id);

        await auditLog.AddAsync(new AuditLog
        {
            UserId = CurrentUserId,
            Action = "Firmware.Delete",
            EntityType = nameof(Firmware),
            EntityId = id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { firmware.Version, firmware.FileName })
        }, ct);

        return NoContent();
    }

    /// <summary>The binary, for an operator in the dashboard.</summary>
    [HttpGet("{id:guid}/file")]
    [Authorize(Roles = "SuperAdmin")] // not Customer: integration detail
    public async Task<IActionResult> DownloadForUser(Guid id, CancellationToken ct)
    {
        var firmware = await firmwares.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Firmware not found", "FIRMWARE_NOT_FOUND");
        var stream = storage.OpenRead(id) ?? throw ApiException.NotFound("No file is stored for this release", "FIRMWARE_FILE_MISSING");
        return File(stream, "application/octet-stream", firmware.FileName);
    }

    /// <summary>
    /// The binary, for a device — no login; the signed link in the MQTT
    /// message is the permission. Works only for the release and deployment it
    /// was issued for, and only until it expires.
    /// </summary>
    [HttpGet("{id:guid}/download")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadForDevice(
        Guid id, [FromQuery] Guid deployment, [FromQuery] long expires, [FromQuery] string? signature, CancellationToken ct)
    {
        if (!downloadLinks.IsValid(id, deployment, expires, signature ?? string.Empty))
        {
            logger.LogWarning("Rejected firmware download for {FirmwareId} (deployment {DeploymentId}): invalid or expired link", id, deployment);
            throw ApiException.Forbidden("This download link is invalid or has expired — retry the deployment to get a new one", "FIRMWARE_LINK_INVALID");
        }

        var target = await deployments.GetByIdAsync(deployment, ct);
        var firmware = await firmwares.GetByIdAsync(id, ct);
        if (target is null || firmware is null || target.FirmwareId != id)
            throw ApiException.NotFound("Firmware not found", "FIRMWARE_NOT_FOUND");

        var stream = storage.OpenRead(id) ?? throw ApiException.NotFound("No file is stored for this release", "FIRMWARE_FILE_MISSING");
        logger.LogInformation("Device {DeviceId} downloading firmware {Version} (deployment {DeploymentId})", target.DeviceId, firmware.Version, deployment);
        return File(stream, "application/octet-stream", firmware.FileName);
    }

    /// <summary>
    /// One deployment per device, each tracked on its own. An Online device is
    /// sent the update now; any other waits and is sent automatically when it
    /// next comes Online (TimeoutSweepWorker).
    /// </summary>
    [HttpPost("deploy")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<IReadOnlyList<FirmwareDeploymentDto>>> Deploy(DeployFirmwareRequest request, CancellationToken ct)
    {
        var firmware = await firmwares.GetByIdAsync(request.FirmwareId, ct) ?? throw ApiException.NotFound("Firmware not found", "FIRMWARE_NOT_FOUND");
        if (!storage.Exists(firmware.Id))
            throw ApiException.BadRequest("This release has no firmware file — create a new release with the file", "FIRMWARE_FILE_MISSING");
        if (request.DeviceIds is not { Count: > 0 })
            throw ApiException.BadRequest("Choose at least one device", "NO_DEVICES_SELECTED");

        var targets = new List<Device>();
        foreach (var deviceId in request.DeviceIds.Distinct())
        {
            var device = await devices.GetByIdAsync(deviceId, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
            RequireTenantAccess(device.TenantId);
            targets.Add(device);
        }

        // Checked for the whole batch before anything is sent, so a bad
        // selection never half-deploys.
        var wrongType = targets.Where(d => d.DeviceTypeId != firmware.DeviceTypeId).Select(d => d.Name).ToList();
        if (wrongType.Count > 0)
            throw ApiException.BadRequest($"This firmware is for a different device type than: {string.Join(", ", wrongType)}", "FIRMWARE_DEVICE_TYPE_MISMATCH");

        var busy = await deployments.DevicesWithActiveDeploymentAsync(targets.Select(d => d.Id).ToList(), ct);
        var busyNames = targets.Where(d => busy.Contains(d.Id)).Select(d => d.Name).ToList();
        if (busyNames.Count > 0)
            throw ApiException.Conflict($"Already updating: {string.Join(", ", busyNames)}. Wait for it to finish first.", "FIRMWARE_UPDATE_IN_PROGRESS");

        var results = new List<FirmwareDeploymentDto>();
        foreach (var device in targets)
        {
            var deployment = await deployments.AddAsync(new FirmwareDeployment
            {
                TenantId = device.TenantId,
                FirmwareId = firmware.Id,
                DeviceId = device.Id,
                Status = FirmwareDeploymentStatus.WaitingForDevice
            }, ct);

            if (device.Status == DeviceStatus.Online)
                await FirmwareDelivery.SendAsync(deployment, device, firmware, dispatcher, deployments, notifier, ct);

            results.Add(Map(deployment));
        }

        return Ok(results);
    }

    /// <summary>Sends a Failed or Timed-out update again, with a fresh download link.</summary>
    [HttpPost("deployments/{id:guid}/retry")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<FirmwareDeploymentDto>> Retry(Guid id, CancellationToken ct)
    {
        var deployment = await deployments.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Deployment not found", "DEPLOYMENT_NOT_FOUND");
        RequireTenantAccess(deployment.TenantId);
        if (deployment.Status is not (FirmwareDeploymentStatus.Failed or FirmwareDeploymentStatus.Timeout))
            throw ApiException.Conflict("Only a failed or timed-out update can be retried", "DEPLOYMENT_NOT_RETRYABLE");

        var firmware = await firmwares.GetByIdAsync(deployment.FirmwareId, ct) ?? throw ApiException.NotFound("Firmware not found", "FIRMWARE_NOT_FOUND");
        if (!storage.Exists(firmware.Id))
            throw ApiException.BadRequest("This release has no firmware file", "FIRMWARE_FILE_MISSING");
        var device = await devices.GetByIdAsync(deployment.DeviceId, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        if ((await deployments.DevicesWithActiveDeploymentAsync([device.Id], ct)).Count > 0)
            throw ApiException.Conflict($"{device.Name} is already being updated", "FIRMWARE_UPDATE_IN_PROGRESS");

        if (device.Status == DeviceStatus.Online)
        {
            await FirmwareDelivery.SendAsync(deployment, device, firmware, dispatcher, deployments, notifier, ct);
        }
        else
        {
            deployment.Status = FirmwareDeploymentStatus.WaitingForDevice;
            deployment.Error = null;
            deployment.StartedAt = null;
            deployment.CompletedAt = null;
            await deployments.UpdateAsync(deployment, ct); // UpdatedAt restarts the wait window
            await notifier.FirmwareDeploymentUpdatedAsync(deployment, ct);
        }

        return Ok(Map(deployment));
    }

    [HttpGet("deployments")]
    [Authorize(Roles = "SuperAdmin")] // not Customer: integration detail
    public async Task<ActionResult<IReadOnlyList<FirmwareDeploymentDto>>> ListDeployments([FromQuery] Guid firmwareId, CancellationToken ct)
    {
        var list = await deployments.ListForFirmwareAsync(firmwareId, ct);
        // Releases are shared across tenants; their deployments are not.
        var tenant = EffectiveTenantFilter(null);
        return Ok(list.Where(d => tenant is null || d.TenantId == tenant).Select(Map).ToList());
    }

    /// <summary>Keeps the uploaded name for the download, minus any path or odd characters.</summary>
    private static string SafeFileName(string uploaded, string version)
    {
        var name = new string(Path.GetFileName(uploaded ?? string.Empty)
            .Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray()).Trim('.', '_');
        if (name.Length == 0) name = $"firmware-{version}.bin";
        return name.Length <= 200 ? name : name[^200..];
    }

    private FirmwareDto Map(Firmware f) =>
        new(f.Id, f.DeviceTypeId, f.Version, f.FileName, f.Sha256Checksum, f.SizeBytes, f.ReleaseNotes, f.ReleaseDate, storage.Exists(f.Id));

    private static FirmwareDeploymentDto Map(FirmwareDeployment d) =>
        new(d.Id, d.FirmwareId, d.DeviceId, d.Status.ToString(), d.AttemptCount, d.Error, d.StartedAt, d.CompletedAt, d.CreatedAt);
}
