using System.Globalization;
using Bms.Application.Abstractions.Payloads;
using Bms.Application.Abstractions.Persistence;
using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;

namespace Bms.Application.Ingestion;

public sealed record NormalizationResult(
    Device? Device,
    NormalizedTelemetry? Telemetry,
    IReadOnlyList<DeviceTypeParameter> Parameters,
    bool HasDeviceTimestamp,
    string? Error,
    TimeSource TimeSource = TimeSource.Device)
{
    public static NormalizationResult Failed(Device? device, string error) => new(device, null, [], false, error);
}

/// <summary>
/// decode -> allow-list filter -> NormalizedTelemetry for one raw message.
/// Devices report through our own gateway, which sends BMS parameter keys
/// (temperature, current, ...), so decoded fields are kept under the names
/// they arrive with — there is no per-vendor mapping step. Shared by the live pipeline and the time-series backfill so a
/// reading rebuilt after an InfluxDB outage is exactly what live ingestion
/// would have written. Infrastructure failures (database unreachable)
/// propagate; problems with the message itself come back as an Error.
/// </summary>
public sealed class TelemetryNormalizer(
    IDeviceRepository devices,
    IDeviceTypeRepository deviceTypes,
    IEnumerable<IPayloadDecoder> decoders)
{
    public async Task<NormalizationResult> NormalizeAsync(RawDeviceMessage rawMessage, CancellationToken ct)
    {
        var device = await devices.GetByIdAsync(rawMessage.DeviceId, ct);
        if (device is null)
            return NormalizationResult.Failed(null, "Unknown device");

        var decoder = decoders.FirstOrDefault(d => d.Format == rawMessage.PayloadFormat);
        if (decoder is null)
            return NormalizationResult.Failed(device, $"No decoder registered for format {rawMessage.PayloadFormat}");

        DecodedPayload decoded;
        try
        {
            decoded = decoder.Decode(rawMessage.RawPayload);
        }
        catch (Exception ex)
        {
            return NormalizationResult.Failed(device, $"Decode failed: {ex.Message}");
        }

        var deviceType = await deviceTypes.GetByIdAsync(device.DeviceTypeId, ct);
        var parameters = deviceType?.Parameters.ToList() ?? [];

        // The Device Type's parameter list is the allow-list: a
        // field whose name isn't a defined parameter for this
        // device's type is silently dropped here rather than stored —
        // this is deliberate (see docs/protocols/normalization.md),
        // not a bug. It lets a device start sending more fields than
        // you've chosen to keep without every extra field landing in
        // InfluxDB.
        // A Derived parameter is kept too when a device sends it: some meters
        // report active power themselves, others only its inputs. A Formula
        // rule calculates the value only for readings that lack it (see
        // FormulaHandler), so the measured value always wins and the two
        // never collide.
        var allowedKeys = parameters
            .Select(p => p.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Only numbers are telemetry: an id or label the gateway includes
        // is never a parameter value. Booleans count as 1/0.
        var filteredFields = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in decoded.Fields)
        {
            if (allowedKeys.Contains(key) && TryToDouble(value, out var numeric))
                filteredFields[key] = numeric;
        }

        // A device that sends a wrong unit is corrected before anything reads
        // the value. The raw message keeps what was sent. A backfill rebuilds
        // with the device's conversions as they are now.
        DeviceValueConversion.Apply(device, filteredFields);

        var deviceTimestamp = decoded.DeviceTimestamp ?? rawMessage.DeviceTimestamp;
        var telemetry = new NormalizedTelemetry
        {
            TenantId = device.TenantId,
            SiteId = device.SiteId,
            DeviceId = device.Id,
            Timestamp = deviceTimestamp ?? rawMessage.ReceivedAt,
            IngestedAt = rawMessage.ReceivedAt,
            CorrelationId = rawMessage.CorrelationId,
            SequenceNumber = decoded.SequenceNumber,
            Fields = filteredFields
        };

        // A time stamped on arrival (by a connector, or by us when none is
        // sent) is not a measurement time, whatever field carried it.
        var timeSource = deviceTimestamp is null || decoded.TimestampIsArrival ? TimeSource.Received : TimeSource.Device;
        return new NormalizationResult(device, telemetry, parameters, deviceTimestamp is not null, null, timeSource);
    }

    private static bool TryToDouble(object? value, out double numeric)
    {
        switch (value)
        {
            case double d:
                numeric = d;
                return true;
            case bool b:
                numeric = b ? 1 : 0;
                return true;
            case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                numeric = parsed;
                return true;
            default:
                numeric = 0;
                return false;
        }
    }
}
