using Bms.Domain.Common;

namespace Bms.Domain.Entities;

public class User : Entity
{
    /// <summary>Null for a SuperAdmin who is not scoped to a single tenant.</summary>
    public Guid? TenantId { get; set; }

    public Guid RoleId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>Obtained by the user messaging the platform's Telegram bot once — see AlertTelegramNotifier. Null means they haven't opted in.</summary>
    public string? TelegramChatId { get; set; }

    public Role? Role { get; set; }
    public Tenant? Tenant { get; set; }
}
