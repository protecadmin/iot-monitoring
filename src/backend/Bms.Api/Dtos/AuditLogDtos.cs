namespace Bms.Api.Dtos;

public sealed record AuditLogDto(
    Guid Id,
    Guid? TenantId,
    string? TenantName,
    Guid? UserId,
    string? UserEmail,
    string Action,
    string? EntityType,
    string? EntityId,
    string? DetailsJson,
    DateTimeOffset CreatedAt);
