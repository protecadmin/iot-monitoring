using Bms.Application.Abstractions.Persistence;
using Bms.Domain;
using Bms.Domain.Alerting;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence.Repositories;

public sealed class AlertRuleRepository(BmsDbContext db) : IAlertRuleRepository
{
    public async Task<IReadOnlyList<AlertRule>> GetApplicableRulesAsync(Guid tenantId, Guid siteId, Guid deviceId, Guid deviceTypeId, CancellationToken ct)
    {
        var own = await db.AlertRules
            .Where(r => r.TenantId == tenantId && r.IsEnabled &&
                        (r.DeviceId == deviceId ||
                         (r.DeviceId == null && r.SiteId == siteId) ||
                         (r.DeviceId == null && r.SiteId == null && (r.DeviceTypeId == null || r.DeviceTypeId == deviceTypeId))))
            .ToListAsync(ct);

        var globals = await db.GlobalAlertRules.AsNoTracking()
            .Where(g => g.IsEnabled && (g.DeviceTypeId == null || g.DeviceTypeId == deviceTypeId))
            .ToListAsync(ct);
        if (globals.Count == 0)
            return own;

        var ids = globals.Select(g => g.Id).ToList();
        var overrides = await db.AlertRuleOverrides.AsNoTracking()
            .Where(o => o.TenantId == tenantId && ids.Contains(o.GlobalAlertRuleId) &&
                        (o.DeviceId == deviceId || (o.DeviceId == null && (o.SiteId == null || o.SiteId == siteId))))
            .ToListAsync(ct);

        own.AddRange(globals
            .Select(g => AlertRuleResolution.Resolve(g, overrides, tenantId, siteId, deviceId))
            .Where(e => e.IsEnabled)
            .Select(e => AlertRuleResolution.ToAlertRule(e, tenantId)));
        return own;
    }

    public async Task<IReadOnlyList<AlertRule>> ListAsync(Guid tenantId, CancellationToken ct) =>
        await db.AlertRules.Where(r => r.TenantId == tenantId).OrderBy(r => r.Name).ToListAsync(ct);

    public Task<AlertRule?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct) =>
        db.AlertRules.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);

    public async Task<AlertRule> AddAsync(AlertRule rule, CancellationToken ct)
    {
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return rule;
    }

    public async Task UpdateAsync(AlertRule rule, CancellationToken ct)
    {
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        db.AlertRules.Update(rule);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        var rule = await GetByIdAsync(tenantId, id, ct);
        if (rule is null) return false;
        db.AlertRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class GlobalAlertRuleRepository(BmsDbContext db) : IGlobalAlertRuleRepository
{
    public async Task<IReadOnlyList<GlobalAlertRule>> ListAsync(CancellationToken ct) =>
        await db.GlobalAlertRules.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);

    public Task<GlobalAlertRule?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.GlobalAlertRules.FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task AddAsync(GlobalAlertRule rule, CancellationToken ct)
    {
        db.GlobalAlertRules.Add(rule);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(GlobalAlertRule rule, CancellationToken ct)
    {
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        db.GlobalAlertRules.Update(rule);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct) =>
        await db.GlobalAlertRules.Where(g => g.Id == id).ExecuteDeleteAsync(ct) > 0;

    public async Task<IReadOnlyList<AlertRuleOverride>> ListOverridesAsync(Guid tenantId, Guid? globalAlertRuleId, CancellationToken ct) =>
        await db.AlertRuleOverrides.AsNoTracking()
            .Where(o => o.TenantId == tenantId && (globalAlertRuleId == null || o.GlobalAlertRuleId == globalAlertRuleId))
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, int>> CountOverridesAsync(CancellationToken ct) =>
        await db.AlertRuleOverrides.AsNoTracking()
            .GroupBy(o => o.GlobalAlertRuleId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public Task<AlertRuleOverride?> GetOverrideAsync(Guid tenantId, Guid globalAlertRuleId, Guid? siteId, Guid? deviceId, CancellationToken ct) =>
        db.AlertRuleOverrides.FirstOrDefaultAsync(o =>
            o.TenantId == tenantId && o.GlobalAlertRuleId == globalAlertRuleId && o.SiteId == siteId && o.DeviceId == deviceId, ct);

    public async Task SaveOverrideAsync(AlertRuleOverride ruleOverride, CancellationToken ct)
    {
        ruleOverride.UpdatedAt = DateTimeOffset.UtcNow;
        if (db.Entry(ruleOverride).State == EntityState.Detached)
            db.AlertRuleOverrides.Add(ruleOverride);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteOverrideAsync(Guid tenantId, Guid globalAlertRuleId, Guid? siteId, Guid? deviceId, CancellationToken ct) =>
        await db.AlertRuleOverrides
            .Where(o => o.TenantId == tenantId && o.GlobalAlertRuleId == globalAlertRuleId && o.SiteId == siteId && o.DeviceId == deviceId)
            .ExecuteDeleteAsync(ct) > 0;

    public async Task<int> ClearRedundantCompanySwitchesAsync(Guid globalAlertRuleId, bool appliesByDefault, CancellationToken ct)
    {
        // Company level only: a site or device switch may be undoing the company's.
        var companyLevel = db.AlertRuleOverrides.Where(o => o.GlobalAlertRuleId == globalAlertRuleId && o.SiteId == null && o.DeviceId == null);
        var cleared = await companyLevel.Where(o => o.IsEnabled == appliesByDefault)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.IsEnabled, (bool?)null).SetProperty(o => o.UpdatedAt, DateTimeOffset.UtcNow), ct);
        await companyLevel.Where(o => o.IsEnabled == null && o.Threshold == null && o.Severity == null && o.CooldownSeconds == null)
            .ExecuteDeleteAsync(ct);
        return cleared;
    }
}

public sealed class AlertRepository(BmsDbContext db) : IAlertRepository
{
    public Task<Alert?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Alerts.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<Alert?> GetLastTriggeredAsync(Guid deviceId, Guid alertRuleId, CancellationToken ct) =>
        db.Alerts
            .Where(a => a.DeviceId == deviceId && a.AlertRuleId == alertRuleId)
            .OrderByDescending(a => a.TriggeredAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Alert>> ListUnresolvedAsync(Guid deviceId, CancellationToken ct) =>
        await db.Alerts.Where(a => a.DeviceId == deviceId && a.Status != AlertStatus.Resolved).ToListAsync(ct);

    public Task<Bms.Application.Common.PagedResult<Alert>> ListAsync(AlertFilter filter, CancellationToken ct)
    {
        var query = db.Alerts.AsNoTracking().AsQueryable();
        if (filter.TenantId is { } tenantId) query = query.Where(a => a.TenantId == tenantId);
        if (filter.DeviceId is { } deviceId) query = query.Where(a => a.DeviceId == deviceId);
        if (filter.Status is { } status) query = query.Where(a => a.Status == status);
        if (filter.Severity is { } severity) query = query.Where(a => a.Severity == severity);

        query = query.OrderByDescending(a => a.TriggeredAt);
        return query.ToPagedResultAsync(filter, ct);
    }

    public async Task<Alert> AddAsync(Alert alert, CancellationToken ct)
    {
        db.Alerts.Add(alert);
        await db.SaveChangesAsync(ct);
        return alert;
    }

    public async Task UpdateAsync(Alert alert, CancellationToken ct)
    {
        alert.UpdatedAt = DateTimeOffset.UtcNow;
        db.Alerts.Update(alert);
        await db.SaveChangesAsync(ct);
    }

}
