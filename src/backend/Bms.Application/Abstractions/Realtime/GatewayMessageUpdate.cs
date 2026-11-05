namespace Bms.Application.Abstractions.Realtime;

public sealed record GatewayMessageUpdate(
    string GatewayCode,
    string Topic,
    DateTimeOffset ReceivedAt,
    string Payload);
