using Bms.Application.Abstractions.Security;

namespace Bms.Infrastructure.Security;

/// <summary>Bcrypt hash in mosquitto-go-auth's expected $2a$/$2b$ format — see IMqttCredentialHasher for why this isn't PBKDF2 like user login passwords.</summary>
public sealed class BcryptMqttCredentialHasher : IMqttCredentialHasher
{
    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
}
