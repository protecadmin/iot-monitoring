using Bms.Application.Abstractions.Persistence;
using Bms.Application.Common;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(BmsDbContext db) : IUserRepository
{
    public Task<User?> GetByEmailAsync(string email, CancellationToken ct) =>
        db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<Role?> GetRoleByNameAsync(string name, CancellationToken ct) =>
        db.Roles.FirstOrDefaultAsync(r => r.Name == name, ct);

    public async Task<User> AddAsync(User user, CancellationToken ct)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task UpdateAsync(User user, CancellationToken ct)
    {
        user.UpdatedAt = DateTimeOffset.UtcNow;
        db.Users.Update(user);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<User>> ListAsync(Guid? tenantId, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking().Include(u => u.Role).AsQueryable();
        if (tenantId is { } id)
            query = query.Where(u => u.TenantId == id);
        return await query.OrderBy(u => u.Email).ToListAsync(ct);
    }
}

public sealed class AuditLogRepository(BmsDbContext db) : IAuditLogRepository
{
    public async Task AddAsync(AuditLog entry, CancellationToken ct)
    {
        db.AuditLogs.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    public Task<PagedResult<AuditLog>> ListAsync(Guid? tenantId, PageRequest request, CancellationToken ct)
    {
        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (tenantId is { } id)
            query = query.Where(a => a.TenantId == id);
        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(a => a.Action.Contains(request.Search));
        query = query.OrderByDescending(a => a.CreatedAt);
        return query.ToPagedResultAsync(request, ct);
    }
}

public sealed class RefreshTokenRepository(BmsDbContext db) : IRefreshTokenRepository
{
    public async Task<RefreshToken> AddAsync(RefreshToken token, CancellationToken ct)
    {
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);
        return token;
    }

    public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct) =>
        db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task UpdateAsync(RefreshToken token, CancellationToken ct)
    {
        db.RefreshTokens.Update(token);
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeAllActiveForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var active = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);
        foreach (var token in active)
            token.RevokedAt = now;
        await db.SaveChangesAsync(ct);
    }
}
