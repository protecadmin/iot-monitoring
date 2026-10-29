using Bms.Application.Common;
using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Persistence;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct);
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Role?> GetRoleByNameAsync(string name, CancellationToken ct);
    Task<User> AddAsync(User user, CancellationToken ct);
    Task UpdateAsync(User user, CancellationToken ct);

    /// <summary>Null tenantId lists every user (SuperAdmin only — see UsersController).</summary>
    Task<IReadOnlyList<User>> ListAsync(Guid? tenantId, CancellationToken ct);
}

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog entry, CancellationToken ct);

    /// <summary>Null tenantId lists across every tenant — only valid for a SuperAdmin caller (see AuditLogsController).</summary>
    Task<PagedResult<AuditLog>> ListAsync(Guid? tenantId, PageRequest request, CancellationToken ct);
}
