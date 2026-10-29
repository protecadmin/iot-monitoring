using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Persistence;

public interface IRefreshTokenRepository
{
    Task<RefreshToken> AddAsync(RefreshToken token, CancellationToken ct);
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct);
    Task UpdateAsync(RefreshToken token, CancellationToken ct);

    /// <summary>Used on reuse-detection (see AuthController.Refresh) and on explicit "log out everywhere".</summary>
    Task RevokeAllActiveForUserAsync(Guid userId, CancellationToken ct);
}
