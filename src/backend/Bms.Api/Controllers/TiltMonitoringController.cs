using System.Globalization;
using Bms.Api.Common;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Tilt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

public sealed record ConfigureTiltRequest(string ClosingTime,string TimeZoneId);

[ApiController]
[Route("api/devices/{deviceId:guid}/tilt")]
[Authorize]
public sealed class TiltMonitoringController(IDeviceRepository devices,ITiltMonitoringService tilt) : BmsControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TiltSummary>> Get(Guid deviceId,[FromQuery]DateOnly? from,[FromQuery]DateOnly? to,CancellationToken ct)
    {
        var device=await devices.GetByIdAsync(deviceId,ct)??throw ApiException.NotFound("Device not found","DEVICE_NOT_FOUND");
        RequireTenantAccess(device.TenantId);
        try{return Ok(await tilt.GetSummaryAsync(device,from,to,ct));}
        catch(ArgumentException ex){throw ApiException.BadRequest(ex.Message,"TILT_SETTINGS_INVALID");}
    }

    [HttpPut]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<TiltSummary>> Configure(Guid deviceId,ConfigureTiltRequest request,CancellationToken ct)
    {
        var device=await devices.GetByIdAsync(deviceId,ct)??throw ApiException.NotFound("Device not found","DEVICE_NOT_FOUND");
        RequireTenantAccess(device.TenantId);
        if(!TimeOnly.TryParseExact(request.ClosingTime,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var time))
            throw ApiException.BadRequest("Closing time must use HH:mm (24-hour time).","TILT_SETTINGS_INVALID");
        try { await tilt.ConfigureAsync(device,time,request.TimeZoneId,ct);return Ok(await tilt.GetSummaryAsync(device,null,null,ct)); }
        catch(Exception ex) when(ex is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {throw ApiException.BadRequest(ex.Message,"TILT_SETTINGS_INVALID");}
    }
}
