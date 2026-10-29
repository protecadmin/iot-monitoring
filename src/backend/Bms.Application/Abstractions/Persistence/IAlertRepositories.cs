using Bms.Application.Common;
using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Persistence;

public interface IAlertRuleRepository
{
    /// <summary>
    /// Every enabled rule that applies to this device: the company's own rules
    /// in scope, plus the global rules for its type resolved through the
    /// company's, site's and device's overrides (see AlertRuleResolution).
    /// A global rule keeps its own id, so cooldowns work the same for both.
    /// </summary>
    Task<IReadOnlyList<AlertRule>> GetApplicableRulesAsync(Guid tenantId, Guid siteId, Guid deviceId, Guid deviceTypeId, CancellationToken ct);
    Task<IReadOnlyList<AlertRule>> ListAsync(Guid tenantId, CancellationToken ct);
    /// <summary>A company's own rule.</summary>
    Task<AlertRule?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct);

    Task<AlertRule> AddAsync(AlertRule rule, CancellationToken ct);
    Task UpdateAsync(AlertRule rule, CancellationToken ct);
    Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken ct);
}

/// <summary>Alert rules defined once for every company, and each company's changes to them.</summary>
public interface IGlobalAlertRuleRepository
{
    Task<IReadOnlyList<GlobalAlertRule>> ListAsync(CancellationToken ct);
    Task<GlobalAlertRule?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(GlobalAlertRule rule, CancellationToken ct);
    Task UpdateAsync(GlobalAlertRule rule, CancellationToken ct);

    /// <summary>Deletes the rule and every company's overrides of it. Past alerts keep their history.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>All of a company's overrides, optionally of one rule.</summary>
    Task<IReadOnlyList<AlertRuleOverride>> ListOverridesAsync(Guid tenantId, Guid? globalAlertRuleId, CancellationToken ct);

    /// <summary>How many companies, sites and devices have changed each rule — keyed by rule id.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountOverridesAsync(CancellationToken ct);

    /// <summary>The override at exactly this level (company: both null; site; device), or null.</summary>
    Task<AlertRuleOverride?> GetOverrideAsync(Guid tenantId, Guid globalAlertRuleId, Guid? siteId, Guid? deviceId, CancellationToken ct);
    Task SaveOverrideAsync(AlertRuleOverride ruleOverride, CancellationToken ct);
    Task<bool> DeleteOverrideAsync(Guid tenantId, Guid globalAlertRuleId, Guid? siteId, Guid? deviceId, CancellationToken ct);

    /// <summary>
    /// Drops company-level on/off switches that only repeat the rule's default,
    /// removing overrides left with nothing else to change. Returns how many changed.
    /// </summary>
    Task<int> ClearRedundantCompanySwitchesAsync(Guid globalAlertRuleId, bool appliesByDefault, CancellationToken ct);
}

public sealed class AlertFilter : PageRequest
{
    public Guid? TenantId { get; init; }
    public Guid? DeviceId { get; init; }
    public Domain.AlertStatus? Status { get; init; }
    public Domain.AlertSeverity? Severity { get; init; }
}

public interface IAlertRepository
{
    Task<Alert?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Alert?> GetLastTriggeredAsync(Guid deviceId, Guid alertRuleId, CancellationToken ct);

    /// <summary>A device's alerts that are still Open or Acknowledged — the ones a recovery can close.</summary>
    Task<IReadOnlyList<Alert>> ListUnresolvedAsync(Guid deviceId, CancellationToken ct);

    Task<PagedResult<Alert>> ListAsync(AlertFilter filter, CancellationToken ct);
    Task<Alert> AddAsync(Alert alert, CancellationToken ct);
    Task UpdateAsync(Alert alert, CancellationToken ct);
}
