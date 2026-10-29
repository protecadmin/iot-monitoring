using System.Globalization;
using Bms.Application.Abstractions.Calculations;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Telemetry;
using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;

namespace Bms.Calculations.Handlers;

/// <summary>
/// BASELINE_DELTA: the reading minus the device's baseline for the source
/// parameter — movement since installation. It is a formula with the baseline
/// as a constant (<c>xDeg - (22.2149)</c>), so live values, history and
/// report periods come from the same code as any formula. History is shown
/// against the current baseline; resetting it re-expresses past readings too.
/// </summary>
public sealed class BaselineDeltaHandler(
    IDeviceBaselineRepository baselines, ICalculationStateRepository states, ICalculatedSeriesStore series) : ICalculationHandler
{
    private readonly FormulaHandler _formula = new(states, series);

    public CalculationType Type => CalculationType.BaselineDelta;

    public string SourceRequirement => "a measured parameter (not Derived)";

    public bool IsEligibleSource(DeviceTypeParameter parameter) => parameter.Kind != ParameterKind.Derived;

    public IReadOnlyList<string> InputKeys(CalculationRule rule) =>
        string.IsNullOrWhiteSpace(rule.SourceParameterKey) ? [] : [rule.SourceParameterKey];

    public async Task<CalculationUpdate?> EvaluateAsync(CalculationRule rule, Device device, NormalizedTelemetry telemetry, CancellationToken ct) =>
        await AsFormulaAsync(rule, device, ct) is { } formula
            ? await _formula.EvaluateAsync(formula, device, telemetry, ct)
            : null;

    public async Task<IReadOnlyList<CalculatedRollupPoint>> RollupAsync(
        CalculationRule rule, Device device, DateTimeOffset from, DateTimeOffset to, string every, CancellationToken ct) =>
        await AsFormulaAsync(rule, device, ct) is { } formula
            ? await _formula.RollupAsync(formula, device, from, to, every, ct)
            : [];

    public async Task<IReadOnlyList<CalculatedPoint>> QueryAsync(
        CalculationRule rule, Device device, DateTimeOffset from, DateTimeOffset to,
        string? aggregateEvery, bool timestampAtWindowStart, CancellationToken ct) =>
        await AsFormulaAsync(rule, device, ct) is { } formula
            ? await _formula.QueryAsync(formula, device, from, to, aggregateEvery, timestampAtWindowStart, ct)
            : [];

    /// <summary>The rule as the formula "source - (baseline)" for this device, or null when it has no baseline.</summary>
    private async Task<CalculationRule?> AsFormulaAsync(CalculationRule rule, Device device, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rule.SourceParameterKey))
            return null;
        if (await baselines.GetAsync(device.Id, rule.SourceParameterKey, ct) is not { } baseline)
            return null;

        // Numbers in a formula always carry a decimal point.
        var constant = baseline.Value.ToString("0.0###############", CultureInfo.InvariantCulture);
        return new CalculationRule
        {
            Id = rule.Id,
            TenantId = rule.TenantId,
            Key = rule.Key,
            Name = rule.Name,
            Type = CalculationType.Formula,
            Expression = $"{rule.SourceParameterKey} - ({constant})",
            OutputKey = rule.OutputKey,
            OutputName = rule.OutputName,
            OutputUnit = rule.OutputUnit
        };
    }
}
