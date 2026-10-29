using Bms.Domain;

namespace Bms.Api.Dtos;

public sealed record CalculationTypeDto(string Type, string Name, string Description, string SourceRequirement);

/// <param name="DefinedOnDeviceType">The rule is this tenant's copy of a Derived parameter's calculation: edited on the Device Types page, only enabled or disabled here.</param>
public sealed record CalculationRuleDto(
    Guid Id, Guid TenantId, Guid? SiteId, Guid? DeviceId, Guid? DeviceTypeId,
    string Name, string Key, string Type, string SourceParameterKey,
    string OutputKey, string OutputName, string? OutputUnit, double? MaxDelta,
    bool IsEnabled, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string AssignmentMode, string? Expression,
    int? WindowSeconds, bool DefinedOnDeviceType = false);

/// <param name="SourceParameterKey">The one parameter the rule reads — required for every type but FORMULA, which reads the parameters its Expression names. Checked in CalculationsController.ValidateAsync.</param>
public sealed record CreateCalculationRuleRequest(
    string Name, string Key, string Type, string? SourceParameterKey,
    string OutputKey, string OutputName, string? OutputUnit, double? MaxDelta,
    Guid? SiteId, Guid? DeviceId, Guid? DeviceTypeId, string? AssignmentMode = null, string? Expression = null,
    int? WindowSeconds = null);

/// <summary>Key and Type are fixed after creation — the key identifies the rule, and changing the type would change what every past result meant.</summary>
public sealed record UpdateCalculationRuleRequest(
    string Name, string? SourceParameterKey,
    string OutputKey, string OutputName, string? OutputUnit, double? MaxDelta,
    Guid? SiteId, Guid? DeviceId, Guid? DeviceTypeId, bool IsEnabled, string? AssignmentMode = null, string? Expression = null,
    int? WindowSeconds = null);

/// <param name="AppliesAutomatically">True for Automatic rules — the device form shows them as already applied rather than selectable.</param>
public sealed record EligibleCalculationRuleDto(CalculationRuleDto Rule, bool AppliesAutomatically);

public sealed record DeviceCalculationsDto(Guid DeviceId, IReadOnlyList<Guid> RuleIds);

public sealed record SetDeviceCalculationsRequest(IReadOnlyList<Guid>? RuleIds);

public sealed record CalculatedPointDto(DateTimeOffset Timestamp, double Value);

public sealed record LatestCalculatedValueDto(Guid RuleId, string OutputKey, string OutputName, string? OutputUnit, double Value, DateTimeOffset Timestamp);

public sealed record CalculationResultsDto(
    Guid RuleId, Guid DeviceId, string OutputKey, string OutputName, string? OutputUnit,
    string Granularity, DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<CalculatedPointDto> Points, double Total, LatestCalculatedValueDto? Latest);

public sealed record CalculationRuleDeviceDto(Guid DeviceId, string DeviceName, string DeviceCode, Guid SiteId, LatestCalculatedValueDto? Latest);

/// <summary>
/// The API names calculation types in SCREAMING_SNAKE_CASE (TOTALIZER_DELTA),
/// distinct from the C# enum member name stored in the database — so the public
/// contract can stay stable if the enum is ever renamed.
/// </summary>
public static class CalculationTypeNames
{
    private static readonly Dictionary<CalculationType, (string Api, string Name, string Description)> Names = new()
    {
        [CalculationType.TotalizerDelta] = (
            "TOTALIZER_DELTA",
            "Totalizer Delta",
            "Current reading minus the previous valid reading of a cumulative counter. Resets never produce negative values."),
        [CalculationType.Formula] = (
            "FORMULA",
            "Formula",
            "Arithmetic over parameters of the same reading, e.g. voltage * current * powerFactor / 1000. Calculated only when the device doesn't send the value itself."),
        [CalculationType.TimeWeightedAverage] = (
            "TIME_WEIGHTED_AVERAGE",
            "Time Weighted Average",
            "The average of a level over a fixed window — a WBGT or dust average. Published once per window, so a limit describes a sustained condition rather than a single reading."),
        [CalculationType.EquivalentContinuousLevel] = (
            "EQUIVALENT_CONTINUOUS_LEVEL",
            "Equivalent Continuous Level (Leq)",
            "The noise level over a fixed window, averaged as sound energy — the number an 8-hour exposure or a boundary noise limit is written against. Not the arithmetic mean of the readings."),
        [CalculationType.BaselineDelta] = (
            "BASELINE_DELTA",
            "Change from baseline",
            "The reading minus the device's baseline (set on the device page, usually at installation) — e.g. how far a tilt meter has moved. No value until a baseline is set."),
    };

    public static string ToApi(CalculationType type) => Names[type].Api;
    public static string DisplayName(CalculationType type) => Names[type].Name;
    public static string Description(CalculationType type) => Names[type].Description;

    public static bool TryParse(string? value, out CalculationType type)
    {
        foreach (var (candidate, names) in Names)
        {
            if (string.Equals(names.Api, value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                type = candidate;
                return true;
            }
        }

        type = default;
        return false;
    }
}
