using System.Text.RegularExpressions;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Common;
using Bms.Domain;
using Bms.Domain.Calculations;
using Bms.Domain.Entities;

namespace Bms.Calculations;

/// <summary>
/// Keeps every tenant's calculation rules in step with the calculations
/// defined on Derived device-type parameters.
///
/// Device types are shared by all tenants but calculation rules belong to
/// one, so a Derived parameter's calculation (DeviceTypeParameter.Calculation*)
/// is the definition and each tenant gets one rule from it: Automatic, scoped
/// to the device type, outputting under the parameter's key. Saving the
/// parameter updates those rules; deleting it, or making it non-Derived,
/// removes them; a tenant created later gets them too.
///
/// A tenant's rule is recognised by device type + output key with no site or
/// device scope, so a rule made earlier on the Calculations page for the
/// whole device type is adopted rather than duplicated.
/// </summary>
public sealed partial class DerivedParameterCalculations(
    ICalculationRuleRepository rules,
    ICalculationStateRepository states,
    ITenantRepository tenants,
    IDeviceTypeRepository deviceTypes,
    IEnumerable<ICalculationHandler> handlers)
{
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$")]
    private static partial Regex OutputKeyPattern();

    /// <summary>
    /// Why <paramref name="parameter"/>'s calculation can't be used on
    /// <paramref name="type"/>, or null when it can. Same checks the
    /// Calculations API makes for a rule.
    /// </summary>
    public string? Validate(DeviceType type, DeviceTypeParameter parameter)
    {
        if (parameter.CalculationType is null)
            return null;
        if (parameter.Kind != ParameterKind.Derived)
            return "Only a Derived parameter can have a calculation";
        if (!OutputKeyPattern().IsMatch(parameter.Key))
            return "A Derived parameter's key must start with a letter and contain only letters, digits or underscores";

        var handler = handlers.FirstOrDefault(h => h.Type == parameter.CalculationType);
        if (handler is null)
            return $"Unsupported calculation type {parameter.CalculationType}";

        var rule = ToRule(type, parameter);
        if (rule.Type == CalculationType.Formula)
        {
            if (!FormulaExpression.TryParse(rule.Expression, out var formula, out var error))
                return $"Formula: {error}";
            if (formula!.Variables.Any(v => CalculationScope.KeyEquals(v, parameter.Key)))
                return "A formula can't use its own output";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(rule.SourceParameterKey))
                return "A source parameter is required";
            if (CalculationScope.KeyEquals(rule.SourceParameterKey, parameter.Key))
                return "The source parameter must differ from the calculated one";
            if (rule.MaxDelta is <= 0)
                return "Max delta must be greater than zero";
            if (rule.Type is CalculationType.TimeWeightedAverage or CalculationType.EquivalentContinuousLevel
                && rule.WindowSeconds is not (>= LevelWindow.MinSeconds and <= LevelWindow.MaxSeconds))
                return $"The averaging window must be between {LevelWindow.MinSeconds} seconds and {LevelWindow.MaxSeconds / 3600} hours";
        }

        var missing = handler.InputKeys(rule).Where(k => !type.Parameters.Any(p => CalculationScope.KeyEquals(p.Key, k))).ToList();
        if (missing.Count > 0)
            return $"{string.Join(", ", missing.Select(k => $"'{k}'"))} {(missing.Count == 1 ? "isn't a parameter" : "aren't parameters")} of {type.Name}";

        if (!CalculationScope.IsEligible(handler, rule, type.Parameters))
            return $"{string.Join(", ", handler.InputKeys(rule).Select(k => $"'{k}'"))} on {type.Name} must each be {handler.SourceRequirement}";

        return null;
    }

    /// <summary>
    /// Brings every tenant's rule for this parameter in line with its
    /// definition — creating, updating or (when it no longer has one)
    /// removing it. Returns a note per tenant that couldn't be updated.
    /// </summary>
    public async Task<IReadOnlyList<string>> ApplyAsync(DeviceType type, DeviceTypeParameter parameter, CancellationToken ct)
    {
        if (parameter.CalculationType is null || parameter.Kind != ParameterKind.Derived)
        {
            await RemoveAsync(type, parameter.Key, ct);
            return [];
        }

        var problems = new List<string>();
        foreach (var tenant in await AllTenantsAsync(ct))
        {
            if (await UpsertAsync(tenant.Id, type, parameter, ct) is { } problem)
                problems.Add($"{tenant.Name}: {problem}");
        }

        return problems;
    }

    /// <summary>Removes every tenant's rule for a parameter that is deleted or no longer calculated.</summary>
    public async Task RemoveAsync(DeviceType type, string parameterKey, CancellationToken ct)
    {
        foreach (var tenant in await AllTenantsAsync(ct))
        {
            if (FindRule(await rules.ListAsync(tenant.Id, ct), type.Id, parameterKey) is { } rule)
                await rules.DeleteAsync(tenant.Id, rule.Id, ct);
        }
    }

    /// <summary>Gives a new tenant a rule for every calculated parameter of every device type.</summary>
    public async Task MaterializeForTenantAsync(Guid tenantId, CancellationToken ct)
    {
        foreach (var type in await deviceTypes.ListAllAsync(ct))
        {
            foreach (var parameter in type.Parameters.Where(p => p.Kind == ParameterKind.Derived && p.CalculationType is not null))
            {
                if (Validate(type, parameter) is null)
                    await UpsertAsync(tenantId, type, parameter, ct);
            }
        }
    }

    private async Task<string?> UpsertAsync(Guid tenantId, DeviceType type, DeviceTypeParameter parameter, CancellationToken ct)
    {
        var tenantRules = await rules.ListAsync(tenantId, ct);
        var wanted = ToRule(type, parameter);

        // Two rules may not publish the same key: another rule already
        // producing it (a different scope, or another device type) wins, and
        // the tenant is reported rather than silently given a second one.
        var existing = FindRule(tenantRules, type.Id, parameter.Key);
        var clash = tenantRules.FirstOrDefault(r => r.Id != existing?.Id && CalculationScope.KeyEquals(r.OutputKey, parameter.Key));
        if (clash is not null)
            return $"calculation rule '{clash.Name}' already outputs '{parameter.Key}'";

        if (existing is null)
        {
            wanted.TenantId = tenantId;
            wanted.Key = UniqueKey(tenantRules, RuleKey(type.Name, parameter.Key));
            await rules.AddAsync(wanted, ct);
            return null;
        }

        // A different calculation makes stored baselines wrong: the next
        // reading starts afresh rather than pairing with an old-basis one.
        var basisChanged = existing.Type != wanted.Type
            || !string.Equals(existing.Expression, wanted.Expression, StringComparison.Ordinal)
            || !CalculationScope.KeyEquals(existing.SourceParameterKey, wanted.SourceParameterKey)
            || existing.MaxDelta != wanted.MaxDelta
            || existing.WindowSeconds != wanted.WindowSeconds;

        existing.Name = wanted.Name;
        existing.Type = wanted.Type;
        existing.Expression = wanted.Expression;
        existing.SourceParameterKey = wanted.SourceParameterKey;
        existing.MaxDelta = wanted.MaxDelta;
        existing.WindowSeconds = wanted.WindowSeconds;
        existing.OutputKey = wanted.OutputKey;
        existing.OutputName = wanted.OutputName;
        existing.OutputUnit = wanted.OutputUnit;
        await rules.UpdateAsync(existing, ct);
        if (basisChanged)
            await states.DeleteForRuleAsync(existing.Id, ct);
        return null;
    }

    /// <summary>
    /// Whether <paramref name="rule"/> is a tenant's copy of a Derived
    /// parameter's calculation on <paramref name="type"/> — defined there,
    /// so only switching it on or off belongs to the tenant.
    /// </summary>
    public static bool IsDefinedBy(DeviceType type, CalculationRule rule) =>
        rule.DeviceTypeId == type.Id && rule.SiteId is null && rule.DeviceId is null
        && type.Parameters.Any(p => p.Kind == ParameterKind.Derived && p.CalculationType is not null
                                    && CalculationScope.KeyEquals(p.Key, rule.OutputKey));

    private static CalculationRule? FindRule(IEnumerable<CalculationRule> tenantRules, Guid deviceTypeId, string parameterKey) =>
        tenantRules.FirstOrDefault(r => r.DeviceTypeId == deviceTypeId && r.SiteId is null && r.DeviceId is null
                                        && CalculationScope.KeyEquals(r.OutputKey, parameterKey));

    private static CalculationRule ToRule(DeviceType type, DeviceTypeParameter p)
    {
        var isFormula = p.CalculationType == CalculationType.Formula;
        var isWindowed = p.CalculationType is CalculationType.TimeWeightedAverage or CalculationType.EquivalentContinuousLevel;
        return new CalculationRule
        {
            DeviceTypeId = type.Id,
            Name = p.Label,
            Type = p.CalculationType ?? CalculationType.Formula,
            Expression = isFormula ? p.CalculationExpression?.Trim() : null,
            SourceParameterKey = isFormula ? string.Empty : (p.CalculationSourceKey ?? string.Empty).Trim(),
            MaxDelta = isFormula ? null : p.CalculationMaxDelta,
            WindowSeconds = isWindowed ? (p.CalculationWindowSeconds ?? LevelWindow.DefaultSeconds) : null,
            OutputKey = p.Key,
            OutputName = p.Label,
            OutputUnit = p.Unit,
            AssignmentMode = CalculationAssignmentMode.Automatic,
            IsEnabled = true
        };
    }

    /// <summary>"Energy Meter" + "activePower" → "energy-meter-active-power", the same key the dashboard used before.</summary>
    public static string RuleKey(string deviceTypeName, string parameterKey)
    {
        static string Slug(string s) => SlugSeparators().Replace(CamelBoundary().Replace(s, "$1-$2").ToLowerInvariant(), "-").Trim('-');
        var key = $"{Slug(deviceTypeName)}-{Slug(parameterKey)}";
        return key.Length > 63 ? key[..63].TrimEnd('-') : key;
    }

    private static string UniqueKey(IReadOnlyList<CalculationRule> tenantRules, string key)
    {
        var candidate = key;
        for (var n = 2; tenantRules.Any(r => r.Key == candidate); n++)
            candidate = $"{key[..Math.Min(key.Length, 60)]}-{n}";
        return candidate;
    }

    private async Task<IReadOnlyList<Tenant>> AllTenantsAsync(CancellationToken ct)
    {
        var all = new List<Tenant>();
        for (var page = 1; ; page++)
        {
            var batch = await tenants.ListAsync(new PageRequest { Page = page, PageSize = 200 }, ct);
            all.AddRange(batch.Items);
            if (batch.Items.Count < 200)
                return all;
        }
    }

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex CamelBoundary();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugSeparators();
}
