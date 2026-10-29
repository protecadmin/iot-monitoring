using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Security;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

/// <summary>
/// Bcrypt specifically (not IPasswordHasher/PBKDF2) because
/// mosquitto-go-auth's Postgres backend verifies against a bcrypt hash —
/// this hasher exists only to produce something that plugin can check,
/// kept separate from user login hashing so the two never get conflated.
/// One-way: there is no Verify here on purpose, only Mosquitto itself
/// ever checks a submitted password against this hash.
/// </summary>
public interface IMqttCredentialHasher
{
    string Hash(string password);
}

/// <summary>
/// AES, reversible — the one piece of this platform's credential storage
/// that isn't one-way, because an MQTT device credential (unlike a user's
/// login password) legitimately needs to be re-displayed to an admin
/// later (e.g. to reconfigure a gateway in the field). The key comes from
/// configuration (never source control) — see EncryptionOptions.
/// </summary>
public interface IReversibleEncryptor
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}

public sealed record AuthTokens(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);

public interface IJwtTokenService
{
    AuthTokens IssueTokens(User user, string roleName);
}

/// <summary>
/// Also implemented by the command-side MQTT publisher for outbound
/// device commands/config/firmware pushes — kept here since Application
/// needs to trigger a send without depending on Bms.Protocols directly.
/// </summary>
public interface IDeviceCommandDispatcher
{
    Task SendCommandAsync(Device device, DeviceCommand command, CancellationToken ct);
    Task SendConfigurationAsync(Device device, DeviceConfiguration configuration, CancellationToken ct);
    Task SendFirmwareDeploymentAsync(Device device, Firmware firmware, FirmwareDeployment deployment, CancellationToken ct);
}
