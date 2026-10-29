using Bms.Application.Abstractions.Calculations;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Telemetry;
using Bms.Domain;
using Bms.Domain.Calculations;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;

namespace Bms.Calculations.Handlers;

/// <summary>
/// FORMULA: an arithmetic formula over parameters of the same reading, such
/// as active power = voltage * current * powerFactor / 1000. Each reading
/// stands alone — no baseline, no pairing with the previous reading.
///
/// A device that measures the value itself wins: when a reading already
/// carries the rule's output key, nothing is calculated for it. So one
/// "activePower" parameter serves meters that report it and meters that only
/// report its inputs.
/// </summary>
public sealed class FormulaHandler(ICalculationStateRepository states, ICalculatedSeriesStore series) : ICalculationHandler
{
    public CalculationType Type => CalculationType.Formula;

    public string SourceRequirement => "a measured parameter (not Derived)";

    // A Derived input would make one formula depend on another rule's output,
    // which isn't in the reading the formula is evaluated against.
    public bool IsEligibleSource(DeviceTypeParameter parameter) => parameter.Kind != ParameterKind.Derived;

    public IReadOnlyList<string> InputKeys(CalculationRule rule) =>
        FormulaExpression.TryParse(rule.Expression, out var formula, out _) ? formula!.Variables : [];

    public async Task<CalculationUpdate?> EvaluateAsync(CalculationRule rule, Device device, NormalizedTelemetry telemetry, CancellationToken ct)
    {
        if (telemetry.Fields.ContainsKey(rule.OutputKey))
            return null;
        if (!FormulaExpression.TryParse(rule.Expression, out var formula, out _))
            return null;
        if (formula!.Evaluate(telemetry.Fields) is not { } value)
            return null;

        // The state row only records the latest value, for the device's live
        // view. A reading older than it (a late or replayed message) is still
        // in history, but must not replace the latest value.
        var state = await states.GetAsync(rule.Id, device.Id, ct);
        if (state is null)
        {
            await states.TryCreateAsync(new CalculationState
            {
                TenantId = device.TenantId,
                CalculationRuleId = rule.Id,
                DeviceId = device.Id,
                LastSourceValue = value,
                LastSourceTimestamp = telemetry.Timestamp,
                LastOutputValue = value,
                LastOutputTimestamp = telemetry.Timestamp
            }, ct);
        }
        else if (telemetry.Timestamp <= state.LastSourceTimestamp
                 || !await states.TryAdvanceAsync(rule.Id, device.Id, state.LastSourceTimestamp, value, telemetry.Timestamp, value, telemetry.Timestamp, ct))
        {
            return null;
        }

        return new CalculationUpdate(
            device.TenantId, device.SiteId, device.Id, rule.Id, rule.Key,
            rule.OutputKey, rule.OutputName, rule.OutputUnit, value, telemetry.Timestamp);
    }

    // A formula yields a level (kW, %): a period is described by its range and average.
    public async Task<IReadOnlyList<CalculatedRollupPoint>> RollupAsync(
        CalculationRule rule, Device device, DateTimeOffset from, DateTimeOffset to, string every, CancellationToken ct)
    {
        if (!FormulaExpression.TryParse(rule.Expression, out var formula, out _))
            return [];

        Task<IReadOnlyList<CalculatedPoint>> Series(string fn) =>
            series.GetFormulaSeriesAsync(device.Id, formula!, rule.OutputKey, from, to, every, timestampAtWindowStart: true, ct, fn);
        var min = Series("min");
        var mean = Series("mean");
        var max = Series("max");
        await Task.WhenAll(min, mean, max);

        var minByPeriod = min.Result.ToDictionary(p => p.Timestamp, p => p.Value);
        var maxByPeriod = max.Result.ToDictionary(p => p.Timestamp, p => p.Value);
        return mean.Result
            .Select(p => new CalculatedRollupPoint(p.Timestamp, minByPeriod.GetValueOrDefault(p.Timestamp, p.Value), p.Value, maxByPeriod.GetValueOrDefault(p.Timestamp, p.Value), null))
            .ToList();
    }

    public Task<IReadOnlyList<CalculatedPoint>> QueryAsync(
        CalculationRule rule, Device device, DateTimeOffset from, DateTimeOffset to,
        string? aggregateEvery, bool timestampAtWindowStart, CancellationToken ct) =>
        FormulaExpression.TryParse(rule.Expression, out var formula, out _)
            ? series.GetFormulaSeriesAsync(device.Id, formula!, rule.OutputKey, from, to, aggregateEvery, timestampAtWindowStart, ct)
            : Task.FromResult<IReadOnlyList<CalculatedPoint>>([]);
}
