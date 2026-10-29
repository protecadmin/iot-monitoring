using Bms.Application.Abstractions.Calculations;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Telemetry;
using Bms.Domain;
using Bms.Domain.Calculations;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;
using Microsoft.Extensions.Logging;

namespace Bms.Calculations.Handlers;

/// <summary>
/// TOTALIZER_DELTA: consumption as the difference between consecutive valid
/// readings of a Counter parameter. The same handler serves an energy meter's
/// kWh totalizer, a water meter's litres or a runtime-hours counter — nothing
/// here knows what the counter measures.
/// </summary>
public sealed class TotalizerDeltaHandler(
    ICalculationStateRepository states,
    ICounterResetRepository counterResets,
    ICalculatedSeriesStore series,
    ILogger<TotalizerDeltaHandler> logger) : ICalculationHandler
{
    public CalculationType Type => CalculationType.TotalizerDelta;

    public string SourceRequirement => "a Counter parameter (a cumulative totalizer)";

    public bool IsEligibleSource(DeviceTypeParameter parameter) => parameter.Kind == ParameterKind.Counter;

    public IReadOnlyList<string> InputKeys(CalculationRule rule) =>
        string.IsNullOrWhiteSpace(rule.SourceParameterKey) ? [] : [rule.SourceParameterKey];

    public async Task<CalculationUpdate?> EvaluateAsync(CalculationRule rule, Device device, NormalizedTelemetry telemetry, CancellationToken ct)
    {
        if (!telemetry.Fields.TryGetValue(rule.SourceParameterKey, out var current))
            return null;

        var state = await states.GetAsync(rule.Id, device.Id, ct);

        // The counter's scaling changed since the baseline (see CounterReset):
        // the old and new readings are on different bases, so their difference
        // isn't consumption. This reading only becomes the new baseline.
        if (state is not null
            && telemetry.Timestamp > state.LastSourceTimestamp
            && await counterResets.AnyBetweenAsync(device.Id, rule.SourceParameterKey, state.LastSourceTimestamp, telemetry.Timestamp, ct))
        {
            await states.TryAdvanceAsync(rule.Id, device.Id, state.LastSourceTimestamp, current, telemetry.Timestamp, null, null, ct);
            logger.LogInformation("{RuleKey} on device {DeviceId}: counter re-based at {Timestamp} after a scaling change — not counted as consumption",
                rule.Key, device.Id, telemetry.Timestamp);
            return null;
        }

        var result = TotalizerDelta.Evaluate(state?.LastSourceValue, state?.LastSourceTimestamp, current, telemetry.Timestamp, rule.MaxDelta);

        if (!result.AdvancesBaseline)
        {
            logger.LogDebug("{RuleKey} ignored {Transition} reading {Value} at {Timestamp} for device {DeviceId}",
                rule.Key, result.Transition, current, telemetry.Timestamp, device.Id);
            return null;
        }

        if (state is null)
        {
            // First reading: it only becomes the baseline. Losing this race to a
            // concurrent message is harmless — the other message set the baseline,
            // and the next reading's delta still covers the whole period.
            await states.TryCreateAsync(new CalculationState
            {
                TenantId = device.TenantId,
                CalculationRuleId = rule.Id,
                DeviceId = device.Id,
                LastSourceValue = current,
                LastSourceTimestamp = telemetry.Timestamp
            }, ct);
            return null;
        }

        var advanced = await states.TryAdvanceAsync(
            rule.Id, device.Id, state.LastSourceTimestamp,
            current, telemetry.Timestamp,
            result.Consumption, result.Consumption is null ? null : telemetry.Timestamp, ct);

        if (!advanced)
        {
            // Another message moved the baseline between our read and write. Not
            // publishing is what prevents double-counting; that message's delta,
            // plus the next one, still add up to the full consumption.
            logger.LogDebug("{RuleKey} lost a concurrent update for device {DeviceId}; skipping reading at {Timestamp}",
                rule.Key, device.Id, telemetry.Timestamp);
            return null;
        }

        if (result.Transition is TotalizerTransition.Decrease or TotalizerTransition.ExceedsMaxDelta)
        {
            logger.LogInformation(
                "{RuleKey} on device {DeviceId}: counter {Transition} from {Previous} to {Current} — starting a new accumulation period instead of counting it",
                rule.Key, device.Id, result.Transition, state.LastSourceValue, current);
        }

        return result.Consumption is { } consumption
            ? new CalculationUpdate(
                device.TenantId, device.SiteId, device.Id, rule.Id, rule.Key,
                rule.OutputKey, rule.OutputName, rule.OutputUnit, consumption, telemetry.Timestamp)
            : null;
    }

    // Consumption is a quantity: a period's figure is its total.
    public async Task<IReadOnlyList<CalculatedRollupPoint>> RollupAsync(
        CalculationRule rule, Device device, DateTimeOffset from, DateTimeOffset to, string every, CancellationToken ct) =>
        (await QueryAsync(rule, device, from, to, every, timestampAtWindowStart: true, ct))
            .Select(p => new CalculatedRollupPoint(p.Timestamp, null, null, null, p.Value))
            .ToList();

    public async Task<IReadOnlyList<CalculatedPoint>> QueryAsync(
        CalculationRule rule, Device device, DateTimeOffset from, DateTimeOffset to,
        string? aggregateEvery, bool timestampAtWindowStart, CancellationToken ct)
    {
        var resets = await counterResets.ListAsync(device.Id, rule.SourceParameterKey, to, ct);
        return await series.GetCounterDeltasAsync(device.Id, rule.SourceParameterKey, from, to, rule.MaxDelta, resets, aggregateEvery, timestampAtWindowStart, ct);
    }
}
