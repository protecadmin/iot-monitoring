namespace Bms.Application.Abstractions.Realtime;

public sealed record HubMessageUpdate(
    string HubId,
    string Topic,
    DateTimeOffset ReceivedAt,
    string Payload);

