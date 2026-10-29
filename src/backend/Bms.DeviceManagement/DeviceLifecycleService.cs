using Bms.Application.Abstractions.DeviceManagement;
using Bms.Application.Abstractions.Notifications;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Realtime;
using Bms.DeviceManagement.Configuration;
using Bms.Domain;
using Bms.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bms.DeviceManagement;

/// <summary>
/// Owns ONLINE/OFFLINE transitions and Last*At tracking (see
/// docs/database/schema.md#device-lifecycle). Heartbeat/telemetry receipt
/// moves a device to ONLINE immediately; going OFFLINE only happens via
/// the periodic sweep, since "no message arrived" can only be noticed by
/// checking the clock, not by an inbound event.
/// </summary>
public sealed class DeviceLifecycleService : IDeviceLifecycleService
{
    private readonly IDeviceRepository _devices;
    private readonly IAlertRuleRepository _alertRules;
    private readonly IAlertRepository _alerts;
    private readonly IRealtimeNotifier _notifier;
    private readonly IAlertEmailNotifier _emailNotifier;
    private readonly IAlertTelegramNotifier _telegramNotifier;
    private readonly DeviceLifecycleOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<DeviceLifecycleService> _logger;

    public DeviceLifecycleService(
        IDeviceRepository devices,
        IAlertRuleRepository alertRules,
        IAlertRepository alerts,
        IRealtimeNotifier notifier,
        IAlertEmailNotifier emailNotifier,
        IAlertTelegramNotifier telegramNotifier,
        IOptions<DeviceLifecycleOptions> options,
        TimeProvider clock,
        ILogger<DeviceLifecycleService> logger)
    {
        _devices = devices;
        _alertRules = alertRules;
        _alerts = alerts;
        _notifier = notifier;
        _emailNotifier = emailNotifier;
        _telegramNotifier = telegramNotifier;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    // Never moved backwards: after an outage the broker replays queued
    // readings carrying their (older) device timestamps, possibly after a
    // fresher heartbeat was already recorded.
    public async Task RecordTelemetryAsync(Device device, DateTimeOffset at, CancellationToken ct)
    {
        device.LastTelemetryAt = Latest(device.LastTelemetryAt, at);
        device.LastSeenAt = Latest(device.LastSeenAt, at);
        await TransitionOnlineIfNeededAsync(device, ct);
        await _devices.UpdateAsync(device, ct);
        await ResolveLifecycleAlertsAsync(device, LifecycleMetrics.All, ct);
    }

    public async Task RecordHeartbeatAsync(Device device, DateTimeOffset at, CancellationToken ct)
    {
        device.LastHeartbeatAt = Latest(device.LastHeartbeatAt, at);
        device.LastSeenAt = Latest(device.LastSeenAt, at);
        await TransitionOnlineIfNeededAsync(device, ct);
        await _devices.UpdateAsync(device, ct);
        // A heartbeat proves the device is alive, not that it is reporting —
        // so it ends Device Offline and Never Connected but leaves No Telemetry open.
        await ResolveLifecycleAlertsAsync(device, [LifecycleMetrics.DeviceOffline, LifecycleMetrics.NeverConnected], ct);
    }

    /// <summary>Closes the device's open lifecycle alerts the message just disproved.</summary>
    private async Task ResolveLifecycleAlertsAsync(Device device, string[] metrics, CancellationToken ct)
    {
        var unresolved = await _alerts.ListUnresolvedAsync(device.Id, ct) ?? [];
        foreach (var alert in unresolved.Where(a => metrics.Contains(a.Parameter)))
        {
            alert.Status = AlertStatus.Resolved;
            alert.ResolvedAt = _clock.GetUtcNow();
            await _alerts.UpdateAsync(alert, ct);
            await _notifier.AlertResolvedAsync(alert, ct);
            _logger.LogInformation("Device {DeviceId} reported again — {Metric} alert {AlertId} resolved", device.Id, alert.Parameter, alert.Id);
        }
    }

    private async Task TransitionOnlineIfNeededAsync(Device device, CancellationToken ct)
    {
        if (device.Status is DeviceStatus.Offline or DeviceStatus.Registered or DeviceStatus.Provisioned)
        {
            _logger.LogInformation("Device {DeviceId} {OldStatus} -> Online", device.Id, device.Status);
            device.Status = DeviceStatus.Online;
            await _notifier.DeviceStatusChangedAsync(device, ct);
        }
    }

    /// <summary>Two reports' worth of silence for a type with a known interval; zero otherwise.</summary>
    private static double MissedReportsSeconds(Device device) => 2.0 * (device.DeviceType?.ExpectedIntervalSeconds ?? 0);

    /// <summary>Silence before a device turns Offline: the platform window, or two missed reports if longer.</summary>
    private double OfflineAfterSeconds(Device device) => Math.Max(_options.OfflineAfterSeconds, MissedReportsSeconds(device));

    /// <summary>A duration for an alert message: "45 s", "12 min", "3.5 h", "4 days".</summary>
    public static string Describe(TimeSpan d) =>
        d.TotalMinutes < 1 ? $"{d.TotalSeconds:0} s"
        : d.TotalHours < 1 ? $"{d.TotalMinutes:0.#} min"
        : d.TotalDays < 2 ? $"{d.TotalHours:0.#} h"
        : $"{d.TotalDays:0.#} days";

    /// <summary>Newest of the given times; nulls are ignored, and all-null gives null.</summary>
    private static DateTimeOffset? Latest(params DateTimeOffset?[] times) => times.Max();

    public async Task<int> SweepOfflineDevicesAsync(DateTimeOffset? silenceTrustedUntil, CancellationToken ct)
    {
        var candidates = await _devices.ListForOfflineSweepAsync(ct);
        var now = _clock.GetUtcNow();

        // While ingestion is down every device looks silent at once, so silence
        // is only credited up to the moment ingestion stopped being trustworthy.
        // A device that fell quiet with the outage simply stops ageing until it
        // ends; one that was already overdue before then is genuinely gone, and
        // still flips — otherwise a long outage would leave devices that have
        // been silent for days sitting there as Online.
        var countSilenceTo = silenceTrustedUntil is { } until && until < now ? until : now;
        var flipped = 0;

        foreach (var device in candidates)
        {
            // Any message proves the device is alive, so the newest one counts.
            // Preferring the heartbeat whenever one exists would flip a device
            // that once sent heartbeats but now only sends telemetry to
            // Offline while its readings are still arriving.
            var lastSeen = Latest(device.LastHeartbeatAt, device.LastTelemetryAt, device.LastSeenAt);
            if (lastSeen is null)
            {
                // Added but never heard from: silence is measured from when it was created.
                if (device.Status is DeviceStatus.Registered or DeviceStatus.Provisioned)
                    await RaiseLifecycleAlertAsync(device, LifecycleMetrics.NeverConnected, countSilenceTo - device.CreatedAt, ct);
                continue;
            }

            var silentFor = countSilenceTo - lastSeen.Value;
            if (silentFor <= TimeSpan.Zero)
                continue; // heard from after ingestion went bad — nothing trustworthy to judge

            if (device.Status is DeviceStatus.Online or DeviceStatus.Warning && silentFor > TimeSpan.FromSeconds(OfflineAfterSeconds(device)))
            {
                _logger.LogWarning("Device {DeviceId} silent for {Seconds}s, marking Offline", device.Id, silentFor.TotalSeconds);
                device.Status = DeviceStatus.Offline;
                await _devices.UpdateAsync(device, ct);
                await _notifier.DeviceStatusChangedAsync(device, ct);
                flipped++;
            }

            // Checked on every sweep while the device stays offline — not only
            // when it flips, which is long before the alert's threshold.
            if (device.Status == DeviceStatus.Offline)
                await RaiseLifecycleAlertAsync(device, LifecycleMetrics.DeviceOffline, silentFor, ct);

            var sinceTelemetry = countSilenceTo - (device.LastTelemetryAt ?? DateTimeOffset.MinValue);
            await RaiseLifecycleAlertAsync(device, LifecycleMetrics.NoTelemetry, sinceTelemetry, ct);
        }

        return flipped;
    }

    private async Task RaiseLifecycleAlertAsync(Device device, string metric, TimeSpan silentDuration, CancellationToken ct)
    {
        if (!device.AlertsEnabled)
            return; // muted — its status still changes, it just raises no alert

        var rules = await _alertRules.GetApplicableRulesAsync(device.TenantId, device.SiteId, device.Id, device.DeviceTypeId, ct);

        foreach (var rule in rules.Where(r => r.IsEnabled && r.Metric == metric))
        {
            var thresholdSeconds = rule.Threshold ?? metric switch
            {
                LifecycleMetrics.DeviceOffline => _options.DeviceOfflineAlertAfterSeconds,
                LifecycleMetrics.NeverConnected => _options.NeverConnectedAlertAfterSeconds,
                _ => _options.NoTelemetryAlertAfterSeconds
            };

            // A slow reporter isn't silent until it has missed two reports.
            if (metric != LifecycleMetrics.NeverConnected)
                thresholdSeconds = Math.Max(thresholdSeconds, MissedReportsSeconds(device));

            if (silentDuration.TotalSeconds < thresholdSeconds)
                continue;

            var last = await _alerts.GetLastTriggeredAsync(device.Id, rule.Id, ct);
            if (last is not null && last.Status != AlertStatus.Resolved)
                continue; // already open — duplicate prevention

            var alert = new Alert
            {
                TenantId = device.TenantId,
                DeviceId = device.Id,
                AlertRuleId = rule.Id,
                Severity = rule.Severity,
                Status = AlertStatus.Open,
                Parameter = metric.ToString(),
                CurrentValue = silentDuration.TotalSeconds,
                Threshold = thresholdSeconds,
                Message = metric == LifecycleMetrics.NeverConnected
                    ? $"{rule.Name}: no message since it was added {Describe(silentDuration)} ago (threshold {Describe(TimeSpan.FromSeconds(thresholdSeconds))})"
                    : $"{rule.Name}: silent for {Describe(silentDuration)} (threshold {Describe(TimeSpan.FromSeconds(thresholdSeconds))})",
                TriggeredAt = _clock.GetUtcNow()
            };

            await _alerts.AddAsync(alert, ct);
            await _notifier.AlertCreatedAsync(alert, ct);
            // Same channels as an alert from a reading (IngestionPipeline) — a
            // device that has gone quiet is exactly when nobody is watching
            // the dashboard. Each is isolated: an SMTP or Telegram outage must
            // not stop the sweep or the other channel.
            await NotifyAsync("email", () => _emailNotifier.NotifyAsync(alert, device, ct));
            await NotifyAsync("Telegram", () => _telegramNotifier.NotifyAsync(alert, device, ct));
        }
    }

    private async Task NotifyAsync(string channel, Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Lifecycle alert {Channel} notification failed", channel);
        }
    }
}
