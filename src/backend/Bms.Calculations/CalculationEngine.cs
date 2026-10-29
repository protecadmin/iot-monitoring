using Bms.Application.Abstractions.Calculations;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Realtime;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;
using Microsoft.Extensions.Logging;

namespace Bms.Calculations;

/// <summary>
/// Runs every applicable Calculation Rule against a freshly persisted reading
/// and pushes each value produced over the existing realtime channel. Rules
/// are isolated from each other: one misconfigured or failing rule must not
/// stop the rest from calculating.
/// </summary>
public sealed class CalculationEngine(
    ICalculationRuleRepository rules,
    IEnumerable<ICalculationHandler> handlers,
    IRealtimeNotifier notifier,
    ILogger<CalculationEngine> logger) : ICalculationEngine
{
    public async Task<IReadOnlyList<CalculationUpdate>> EvaluateAsync(Device device, IReadOnlyList<DeviceTypeParameter> parameters, NormalizedTelemetry telemetry, CancellationToken ct)
    {
        var applicable = await rules.GetApplicableRulesAsync(device.TenantId, device.SiteId, device.Id, device.DeviceTypeId, ct);
        var produced = new List<CalculationUpdate>();

        foreach (var rule in applicable)
        {
            var handler = handlers.FirstOrDefault(h => h.Type == rule.Type);
            if (handler is null)
            {
                logger.LogWarning("No calculation handler registered for {Type} (rule {RuleKey})", rule.Type, rule.Key);
                continue;
            }

            // Cheap skip before any database work — most readings don't carry
            // every input of most rules.
            var inputs = handler.InputKeys(rule);
            if (inputs.Count == 0 || !inputs.All(telemetry.Fields.ContainsKey))
                continue;

            if (!CalculationScope.IsEligible(handler, rule, parameters))
            {
                logger.LogDebug("Rule {RuleKey} skipped for device {DeviceId}: inputs {Inputs} must each be {Requirement}",
                    rule.Key, device.Id, string.Join(", ", inputs), handler.SourceRequirement);
                continue;
            }

            try
            {
                var update = await handler.EvaluateAsync(rule, device, telemetry, ct);
                if (update is not null)
                {
                    produced.Add(update);
                    await notifier.CalculationUpdatedAsync(update, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Calculation rule {RuleKey} failed for device {DeviceId}", rule.Key, device.Id);
            }
        }

        return produced;
    }
}
