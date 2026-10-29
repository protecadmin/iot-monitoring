namespace Bms.Api.Dtos;

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken, UserDto User);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string? RefreshToken);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record UserDto(Guid Id, string Email, string DisplayName, string Role, Guid? TenantId);

public sealed record ManagedUserDto(Guid Id, string Email, string DisplayName, string Role, Guid? TenantId, bool IsActive, DateTimeOffset CreatedAt, string? TelegramChatId);

public sealed record CreateUserRequest(string Email, string DisplayName, string Password, string Role, Guid? TenantId, string? TelegramChatId);

// Role and Password are optional — omit (or send empty) to keep the current value.
public sealed record UpdateUserRequest(string DisplayName, string? TelegramChatId, string? Role = null, string? Password = null);
