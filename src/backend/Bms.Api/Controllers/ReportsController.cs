using System.Text;
using Bms.Api.Common;
using Bms.Application.Abstractions.Calculations;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Telemetry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

/// <summary>
/// CSV export, implemented end-to-end. Excel/PDF (core.txt section 52)
/// are not implemented — see docs/troubleshooting/common-issues.md's
/// known-limitations note; CSV covers the same underlying queries and
/// opens directly in Excel.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize]
public sealed class ReportsController(
    ITelemetryStore telemetryStore, ICalculationReader calculations, IAlertRepository alerts,
    IDeviceRepository devices, IDeviceTypeRepository deviceTypes) : BmsControllerBase
{
    [HttpGet("telemetry")]
    public async Task<IActionResult> Telemetry([FromQuery] Guid deviceId, [FromQuery] TelemetryRange range = TelemetryRange.TwentyFourHours, CancellationToken ct = default)
    {
        var device = await devices.GetByIdAsync(deviceId, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        RequireTenantAccess(device.TenantId);

        var points = await telemetryStore.GetHistoryAsync(deviceId, range, ct);
        var derived = await calculations.GetHistoryAsync(device, range, ct);

        // Rows hold what was measured and what was calculated from it, on the
        // same timestamps the dashboard uses. A key both measured and
        // calculated (a meter that reports active power itself, another that
        // doesn't) is one column: the measured value wins, as everywhere else.
        var rows = new SortedDictionary<DateTimeOffset, Dictionary<string, double>>();
        Dictionary<string, double> Row(DateTimeOffset at) =>
            rows.TryGetValue(at, out var row) ? row : rows[at] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in points)
            foreach (var (key, value) in p.Fields)
                Row(p.Timestamp)[key] = value;
        foreach (var (key, series) in derived)
            foreach (var value in series)
                Row(value.Timestamp).TryAdd(key, value.Value);

        // Columns are the parameters the device's type defines now, in the
        // type's own order — a parameter deleted from the type no longer
        // appears, matching the dashboard.
        var definedKeys = ((await deviceTypes.GetByIdAsync(device.DeviceTypeId, ct))?.Parameters ?? []).Select(p => p.Key).ToList();
        var present = rows.Values.SelectMany(r => r.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var columns = definedKeys.Where(present.Contains).ToList();

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', new[] { "timestamp" }.Concat(columns.Select(CsvCell))));
        foreach (var (timestamp, row) in rows)
        {
            if (!columns.Any(row.ContainsKey))
                continue; // only carried fields the type no longer defines
            var cells = columns.Select(c => row.TryGetValue(c, out var v) ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
            csv.AppendLine(string.Join(',', new[] { timestamp.ToString("O") }.Concat(cells)));
        }

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"telemetry-{device.DeviceCode}-{range}.csv");
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts([FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var effectiveTenantId = EffectiveTenantFilter(tenantId);
        var result = await alerts.ListAsync(new AlertFilter { TenantId = effectiveTenantId, PageSize = 5000 }, ct);

        var csv = new StringBuilder();
        csv.AppendLine("triggeredAt,deviceId,severity,status,parameter,currentValue,threshold,message");
        foreach (var a in result.Items)
        {
            csv.AppendLine(string.Join(',',
                a.TriggeredAt.ToString("O"), a.DeviceId, a.Severity, a.Status, a.Parameter, a.CurrentValue, a.Threshold,
                $"\"{a.Message.Replace("\"", "\"\"")}\""));
        }

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "alerts-report.csv");
    }

    /// <summary>Parameter keys are user-defined, so a key with a comma or quote is quoted as CSV requires.</summary>
    private static string CsvCell(string value) =>
        value.IndexOfAny([',', '"', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
