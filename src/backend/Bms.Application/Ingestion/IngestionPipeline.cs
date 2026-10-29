using System.Diagnostics;
using System.Globalization;
using Bms.Application.Abstractions.Alerting;
using Bms.Application.Abstractions.Calculations;
using Bms.Application.Abstractions.DeviceManagement;
using Bms.Application.Abstractions.Notifications;
using Bms.Application.Abstractions.Payloads;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Realtime;
using Bms.Application.Abstractions.Telemetry;
using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bms.Application.Ingestion;

public sealed class IngestionPipeline : IIngestionPipeline
{
    private readonly IRawMessageRepository _rawMessages;
    private readonly TelemetryNormalizer _normalizer;
    private readonly ITelemetryStore _telemetryStore;
    private readonly IAlertEvaluator _alertEvaluator;
    private readonly IDeviceLifecycleService _deviceLifecycle;
    private readonly IRealtimeNotifier _notifier;
    private readonly IAlertEmailNotifier _alertEmailNotifier;
    private readonly IAlertTelegramNotifier _alertTelegramNotifier;
    private readonly ICalculationEngine _calculations;
    private readonly ITransientFailureClassifier _transientFailures;
    private readonly IngestionReliabilityOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<IngestionPipeline> _logger;

    public IngestionPipeline(
        IDeviceRepository devices,
        IDeviceTypeRepository deviceTypes,
        IRawMessageRepository rawMessages,
        IEnumerable<IPayloadDecoder> decoders,
        ITelemetryStore telemetryStore,
        IAlertEvaluator alertEvaluator,
        IDeviceLifecycleService deviceLifecycle,
        IRealtimeNotifier notifier,
        IAlertEmailNotifier alertEmailNotifier,
        IAlertTelegramNotifier alertTelegramNotifier,
        ICalculationEngine calculations,
        ITransientFailureClassifier transientFailures,
        IOptions<IngestionReliabilityOptions> options,
        TimeProvider clock,
        ILogger<IngestionPipeline> logger)
    {
        _rawMessages = rawMessages;
        _normalizer = new TelemetryNormalizer(devices, deviceTypes, decoders);
        _telemetryStore = telemetryStore;
        _alertEvaluator = alertEvaluator;
        _deviceLifecycle = deviceLifecycle;
        _notifier = notifier;
        _alertEmailNotifier = alertEmailNotifier;
        _alertTelegramNotifier = alertTelegramNotifier;
        _calculations = calculations;
        _transientFailures = transientFailures;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public Task ProcessAsync(RawDeviceMessage rawMessage, CancellationToken ct) => ProcessAsync(rawMessage, null, ct);

    public async Task ProcessAsync(RawDeviceMessage rawMessage, Func<CancellationToken, Task>? onDurablyRecorded, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();

        // Everything up to and including the PostgreSQL insert decides
        // whether the sender may forget this message. A transient
        // infrastructure failure in this part propagates out of the
        // pipeline un-acknowledged (the caller retries, and MQTT redelivers
        // after a reconnect); only a failure that is about the message
        // itself is recorded as Rejected and acknowledged.
        Accepted? accepted;
        try
        {
            accepted = await RecordAsync(rawMessage, ct);
        }
        catch (Exception ex) when (!_transientFailures.IsTransient(ex) && !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Ingestion pipeline failed for message {CorrelationId} device {DeviceId}",
                rawMessage.CorrelationId, rawMessage.DeviceId);
            await RejectAsync(rawMessage, $"Unhandled pipeline error: {ex.Message}", ct);
            accepted = null;
        }

        if (onDurablyRecorded is not null)
            await onDurablyRecorded(ct);

        if (accepted is not null)
            await RunDownstreamAsync(rawMessage, accepted, ct);

        IngestionMetrics.ProcessingLatencyMs.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private sealed record Accepted(Device Device, NormalizedTelemetry Telemetry, IReadOnlyList<DeviceTypeParameter> Parameters, TimeSource TimeSource);

    /// <returns>The accepted reading, or null when the message was recorded as Rejected/Duplicate.</returns>
    private async Task<Accepted?> RecordAsync(RawDeviceMessage rawMessage, CancellationToken ct)
    {
        var normalized = await _normalizer.NormalizeAsync(rawMessage, ct);
        if (normalized.Error is not null)
        {
            await RejectAsync(rawMessage, normalized.Error, ct);
            return null;
        }

        var device = normalized.Device!;
        var telemetry = normalized.Telemetry!;
        rawMessage.SequenceNumber = telemetry.SequenceNumber;
        rawMessage.DeviceTimestamp = telemetry.Timestamp;
        rawMessage.DedupKey = BuildDedupKey(telemetry, normalized.HasDeviceTimestamp);

        var now = _clock.GetUtcNow();
        var validationError = TelemetryValidator.Validate(device, telemetry, normalized.Parameters, now);
        if (validationError is not null)
        {
            await RejectAsync(rawMessage, validationError, ct);
            return null;
        }

        if (await IsDuplicateAsync(rawMessage, now, ct))
        {
            await RecordDuplicateAsync(rawMessage, ct);
            return null;
        }

        // From here on the message is genuinely valid, deduplicated,
        // and about to be recorded as Persisted exactly once — the unique
        // index on (DeviceId, DedupKey) settles any race the check above
        // lost. A failure in what follows (InfluxDB unreachable, an alert
        // rule bug, a notify failure) is an infrastructure/downstream
        // problem, not a reason to re-decide this message's own validity.
        rawMessage.ProcessingStatus = MessageProcessingStatus.Persisted;
        if (!await _rawMessages.TryAddPersistedAsync(rawMessage, ct))
        {
            await RecordDuplicateAsync(rawMessage, ct);
            return null;
        }

        IngestionMetrics.MessagesPersisted.Add(1);
        return new Accepted(device, telemetry, normalized.Parameters, normalized.TimeSource);
    }

    private async Task RunDownstreamAsync(RawDeviceMessage rawMessage, Accepted accepted, CancellationToken ct)
    {
        var (device, telemetry, parameters, timeSource) = accepted;
        // Saved with the lifecycle update just below, which writes the device.
        device.TimeSource = timeSource;

        // Each downstream effect is isolated: an InfluxDB outage must
        // not also prevent device-status tracking or alert evaluation
        // (arguably more important during an outage than the chart),
        // and vice versa. See core.txt section 53 (InfluxDB
        // unavailable / PostgreSQL unavailable failure scenarios).
        // A reading that misses InfluxDB keeps TimeSeriesWrittenAt null and
        // is written later by the time-series backfill.
        await TryAsync(async () =>
        {
            try
            {
                await _telemetryStore.WriteAsync(telemetry, ct);
            }
            catch
            {
                IngestionMetrics.TimeSeriesWriteFailures.Add(1);
                throw;
            }
            await _rawMessages.MarkTimeSeriesWrittenAsync(rawMessage.Id, _clock.GetUtcNow(), ct);
        }, rawMessage, "InfluxDB write");
        await TryAsync(() => _deviceLifecycle.RecordTelemetryAsync(device, telemetry.Timestamp, ct), rawMessage, "device lifecycle update");
        await TryAsync(() => _notifier.TelemetryUpdatedAsync(telemetry, ct), rawMessage, "telemetry real-time notify");

        // Derived values come from the reading just persisted, so a
        // calculation can never run ahead of its raw data. Isolated like
        // every other effect here: a calculation fault must not block
        // alert evaluation below.
        IReadOnlyList<CalculationUpdate> calculated = [];
        await TryAsync(async () => calculated = await _calculations.EvaluateAsync(device, parameters, telemetry, ct), rawMessage, "calculation evaluation");

        // Alert rules see the calculated values too (activePower > 50 kW),
        // alongside what the device measured — a measured value wins.
        var forAlerts = calculated is not { Count: > 0 } ? telemetry : telemetry.WithCalculated(calculated.Select(c => KeyValuePair.Create(c.OutputKey, c.Value)));

        await TryAsync(async () =>
        {
            var alerts = await _alertEvaluator.EvaluateAsync(device, forAlerts, ct) ?? [];
            foreach (var alert in alerts)
            {
                // Recovered: the bell drops it. Email and Telegram only hear
                // about new alerts.
                if (alert.Status == AlertStatus.Resolved)
                {
                    await _notifier.AlertResolvedAsync(alert, ct);
                    continue;
                }

                await _notifier.AlertCreatedAsync(alert, ct);
                // Isolated from the SignalR push above and from each
                // other — an SMTP or Telegram outage must not prevent
                // the in-app notification or the other channel.
                await TryAsync(() => _alertEmailNotifier.NotifyAsync(alert, device, ct), rawMessage, "alert email notify");
                await TryAsync(() => _alertTelegramNotifier.NotifyAsync(alert, device, ct), rawMessage, "alert telegram notify");
            }
        }, rawMessage, "alert evaluation");
    }

    /// <summary>
    /// Built only from what the device itself sent, so a broker redelivery
    /// (byte-identical) yields the same key while a later reading — even one
    /// reusing a wrapped or reset sequence number — does not, because its
    /// device timestamp differs. Without a device timestamp there is no such
    /// identity: see <see cref="IsDuplicateAsync"/>.
    /// </summary>
    public static string? BuildDedupKey(NormalizedTelemetry telemetry, bool hasDeviceTimestamp)
    {
        if (!hasDeviceTimestamp)
            return null;

        var ts = telemetry.Timestamp.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        return telemetry.SequenceNumber is { } seq
            ? $"s{seq.ToString(CultureInfo.InvariantCulture)}@{ts}"
            : $"@{ts}";
    }

    private async Task<bool> IsDuplicateAsync(RawDeviceMessage rawMessage, DateTimeOffset now, CancellationToken ct)
    {
        if (rawMessage.DedupKey is { } key)
            return await _rawMessages.ExistsByDedupKeyAsync(rawMessage.DeviceId, key, ct);

        // Sequence number but no device timestamp: a redelivery is
        // byte-identical and arrives within the broker's session lifetime.
        // Matching on sequence alone would discard legitimate readings once
        // a 16-bit counter wraps or the device reboots and restarts at 0.
        if (rawMessage.SequenceNumber is { } seq)
            return await _rawMessages.ExistsRecentAsync(
                rawMessage.DeviceId, seq, rawMessage.RawPayload, now.AddHours(-_options.DuplicateWindowHours), ct);

        // Neither: nothing distinguishes a redelivery from a new reading
        // that happens to carry the same values, so nothing is dropped.
        return false;
    }

    private async Task RecordDuplicateAsync(RawDeviceMessage rawMessage, CancellationToken ct)
    {
        IngestionMetrics.MessagesDuplicate.Add(1);
        await RejectAsync(rawMessage, "Duplicate telemetry (same device + sequence/timestamp already processed)", ct, countAsRejected: false);
    }

    private async Task TryAsync(Func<Task> action, RawDeviceMessage rawMessage, string stepName)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Step} failed for message {CorrelationId} device {DeviceId} (raw message already recorded as Persisted)",
                stepName, rawMessage.CorrelationId, rawMessage.DeviceId);
        }
    }

    private async Task RejectAsync(RawDeviceMessage rawMessage, string error, CancellationToken ct, bool countAsRejected = true)
    {
        rawMessage.ProcessingStatus = MessageProcessingStatus.Rejected;
        rawMessage.ProcessingError = error;
        await _rawMessages.AddAsync(rawMessage, ct);
        if (countAsRejected)
            IngestionMetrics.MessagesRejected.Add(1);
        _logger.LogWarning("Rejected message {CorrelationId} for device {DeviceId}: {Reason}",
            rawMessage.CorrelationId, rawMessage.DeviceId, error);
    }
}
