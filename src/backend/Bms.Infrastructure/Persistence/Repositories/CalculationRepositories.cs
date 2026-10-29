using Bms.Application.Abstractions.Persistence;
using Bms.Domain;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bms.Infrastructure.Persistence.Repositories;

public sealed class CalculationRuleRepository(BmsDbContext db) : ICalculationRuleRepository
{
    // Same predicate as Bms.Calculations.CalculationScope.Matches — every scope
    // field the rule sets has to match the device.
    public async Task<IReadOnlyList<CalculationRule>> GetApplicableRulesAsync(Guid tenantId, Guid siteId, Guid deviceId, Guid deviceTypeId, CancellationToken ct) =>
        await db.CalculationRules
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.IsEnabled &&
                        (r.SiteId == null || r.SiteId == siteId) &&
                        (r.DeviceId == null || r.DeviceId == deviceId) &&
                        (r.DeviceTypeId == null || r.DeviceTypeId == deviceTypeId) &&
                        (r.AssignmentMode == CalculationAssignmentMode.Automatic ||
                         db.DeviceCalculationRules.Any(a => a.CalculationRuleId == r.Id && a.DeviceId == deviceId)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CalculationRule>> ListAsync(Guid tenantId, CancellationToken ct) =>
        await db.CalculationRules.AsNoTracking().Where(r => r.TenantId == tenantId).OrderBy(r => r.Name).ToListAsync(ct);

    public Task<CalculationRule?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct) =>
        db.CalculationRules.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);

    public Task<CalculationRule?> GetByKeyAsync(Guid tenantId, string key, CancellationToken ct) =>
        db.CalculationRules.AsNoTracking().FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Key == key, ct);

    public async Task<CalculationRule> AddAsync(CalculationRule rule, CancellationToken ct)
    {
        db.CalculationRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return rule;
    }

    public async Task UpdateAsync(CalculationRule rule, CancellationToken ct)
    {
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        db.CalculationRules.Update(rule);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        var rule = await GetByIdAsync(tenantId, id, ct);
        if (rule is null) return false;
        db.CalculationRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class CalculationStateRepository(BmsDbContext db) : ICalculationStateRepository
{
    public Task<CalculationState?> GetAsync(Guid ruleId, Guid deviceId, CancellationToken ct) =>
        db.CalculationStates.AsNoTracking().FirstOrDefaultAsync(s => s.CalculationRuleId == ruleId && s.DeviceId == deviceId, ct);

    public async Task<bool> TryCreateAsync(CalculationState state, CancellationToken ct)
    {
        state.LastSourceTimestamp = state.LastSourceTimestamp.ToUniversalTime();
        db.CalculationStates.Add(state);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another message for the same rule and device inserted the baseline first.
            db.Entry(state).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> TryAdvanceAsync(
        Guid ruleId, Guid deviceId, DateTimeOffset expectedSourceTimestamp,
        double newSourceValue, DateTimeOffset newSourceTimestamp,
        double? outputValue, DateTimeOffset? outputTimestamp, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var newTimestamp = newSourceTimestamp.ToUniversalTime();

        // A single conditional UPDATE, so reading and advancing the baseline is
        // atomic in the database: if a concurrent message already moved it, the
        // WHERE matches no row and this reading publishes nothing.
        var candidate = db.CalculationStates.Where(s =>
            s.CalculationRuleId == ruleId && s.DeviceId == deviceId && s.LastSourceTimestamp == expectedSourceTimestamp);

        var affected = outputValue is { } value && outputTimestamp is { } at
            ? await candidate.ExecuteUpdateAsync(u => u
                .SetProperty(s => s.LastSourceValue, newSourceValue)
                .SetProperty(s => s.LastSourceTimestamp, newTimestamp)
                .SetProperty(s => s.LastOutputValue, value)
                .SetProperty(s => s.LastOutputTimestamp, at.ToUniversalTime())
                .SetProperty(s => s.UpdatedAt, now), ct)
            : await candidate.ExecuteUpdateAsync(u => u
                .SetProperty(s => s.LastSourceValue, newSourceValue)
                .SetProperty(s => s.LastSourceTimestamp, newTimestamp)
                .SetProperty(s => s.UpdatedAt, now), ct);

        return affected == 1;
    }

    public async Task<IReadOnlyList<CalculationState>> ListWithRulesForDevicesAsync(IReadOnlyCollection<Guid> deviceIds, CancellationToken ct) =>
        await db.CalculationStates
            .AsNoTracking()
            .Include(s => s.CalculationRule)
            .Where(s => deviceIds.Contains(s.DeviceId))
            .ToListAsync(ct);

    public Task DeleteForRuleAsync(Guid ruleId, CancellationToken ct) =>
        db.CalculationStates.Where(s => s.CalculationRuleId == ruleId).ExecuteDeleteAsync(ct);
}

public sealed class DeviceCalculationAssignmentRepository(BmsDbContext db) : IDeviceCalculationAssignmentRepository
{
    public async Task<IReadOnlyList<Guid>> GetRuleIdsForDeviceAsync(Guid deviceId, CancellationToken ct) =>
        await db.DeviceCalculationRules.AsNoTracking().Where(a => a.DeviceId == deviceId).Select(a => a.CalculationRuleId).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetDeviceIdsForRuleAsync(Guid ruleId, CancellationToken ct) =>
        await db.DeviceCalculationRules.AsNoTracking().Where(a => a.CalculationRuleId == ruleId).Select(a => a.DeviceId).ToListAsync(ct);

    public Task<bool> IsAssignedAsync(Guid ruleId, Guid deviceId, CancellationToken ct) =>
        db.DeviceCalculationRules.AnyAsync(a => a.CalculationRuleId == ruleId && a.DeviceId == deviceId, ct);

    public async Task ReplaceForDeviceAsync(Guid tenantId, Guid deviceId, IReadOnlyCollection<Guid> ruleIds, CancellationToken ct)
    {
        var existing = await db.DeviceCalculationRules.Where(a => a.DeviceId == deviceId).ToListAsync(ct);
        var wanted = ruleIds.ToHashSet();

        var removed = existing.Where(a => !wanted.Contains(a.CalculationRuleId)).ToList();
        db.DeviceCalculationRules.RemoveRange(removed);

        foreach (var ruleId in wanted.Except(existing.Select(a => a.CalculationRuleId)))
            db.DeviceCalculationRules.Add(new DeviceCalculationRule { TenantId = tenantId, DeviceId = deviceId, CalculationRuleId = ruleId });

        await db.SaveChangesAsync(ct);

        var removedRuleIds = removed.Select(a => a.CalculationRuleId).ToList();
        if (removedRuleIds.Count > 0)
            await db.CalculationStates
                .Where(st => st.DeviceId == deviceId && removedRuleIds.Contains(st.CalculationRuleId))
                .ExecuteDeleteAsync(ct);
    }
}
