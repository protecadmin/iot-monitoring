using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Persistence;

public interface ICalculationRuleRepository
{
    /// <summary>
    /// Enabled rules in the tenant whose every set scope field (site, device, device type) matches this device,
    /// and which are either Automatic or have been selected for this device.
    /// </summary>
    Task<IReadOnlyList<CalculationRule>> GetApplicableRulesAsync(Guid tenantId, Guid siteId, Guid deviceId, Guid deviceTypeId, CancellationToken ct);
    Task<IReadOnlyList<CalculationRule>> ListAsync(Guid tenantId, CancellationToken ct);
    Task<CalculationRule?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct);
    Task<CalculationRule?> GetByKeyAsync(Guid tenantId, string key, CancellationToken ct);
    Task<CalculationRule> AddAsync(CalculationRule rule, CancellationToken ct);
    Task UpdateAsync(CalculationRule rule, CancellationToken ct);
    Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken ct);
}

public interface ICalculationStateRepository
{
    Task<CalculationState?> GetAsync(Guid ruleId, Guid deviceId, CancellationToken ct);

    /// <summary>Inserts the first baseline for a rule and device. False when another message created it concurrently.</summary>
    Task<bool> TryCreateAsync(CalculationState state, CancellationToken ct);

    /// <summary>
    /// Compare-and-swap. Moves the baseline to the new reading only if it still
    /// sits at <paramref name="expectedSourceTimestamp"/>. False means another
    /// message moved it first, and the caller must not publish a value — this
    /// is what stops one reading being counted twice under concurrent ingestion.
    /// A null <paramref name="outputValue"/> leaves the last published value untouched.
    /// </summary>
    Task<bool> TryAdvanceAsync(
        Guid ruleId,
        Guid deviceId,
        DateTimeOffset expectedSourceTimestamp,
        double newSourceValue,
        DateTimeOffset newSourceTimestamp,
        double? outputValue,
        DateTimeOffset? outputTimestamp,
        CancellationToken ct);

    Task<IReadOnlyList<CalculationState>> ListWithRulesForDevicesAsync(IReadOnlyCollection<Guid> deviceIds, CancellationToken ct);

    /// <summary>Discards every device's baseline for a rule, so the next reading starts a fresh accumulation period.</summary>
    Task DeleteForRuleAsync(Guid ruleId, CancellationToken ct);
}

public interface IDeviceCalculationAssignmentRepository
{
    Task<IReadOnlyList<Guid>> GetRuleIdsForDeviceAsync(Guid deviceId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetDeviceIdsForRuleAsync(Guid ruleId, CancellationToken ct);
    Task<bool> IsAssignedAsync(Guid ruleId, Guid deviceId, CancellationToken ct);

    /// <summary>
    /// Makes <paramref name="ruleIds"/> exactly the Selected rules on this device. A rule taken off
    /// the device loses its live baseline, so selecting it again later starts a fresh accumulation
    /// period instead of counting everything consumed in between as a single delta.
    /// </summary>
    Task ReplaceForDeviceAsync(Guid tenantId, Guid deviceId, IReadOnlyCollection<Guid> ruleIds, CancellationToken ct);
}
