namespace Bms.Api.Dtos;

public sealed record MqttCredentialDto(
    Guid Id, Guid TenantId, string TenantName, string Username, string Password, DateTimeOffset CreatedAt, DateTimeOffset? RotatedAt);

/// <summary>Sets a chosen password instead of a generated one — for devices shipped with a fixed credential.</summary>
public sealed record SetMqttPasswordRequest(string Password);
