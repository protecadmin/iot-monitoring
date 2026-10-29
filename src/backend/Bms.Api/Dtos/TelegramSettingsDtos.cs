namespace Bms.Api.Dtos;

public sealed record TelegramSettingsDto(bool HasBotToken);

// Token is optional on write — omit to keep whatever's already stored, send an empty string to clear it.
public sealed record UpdateTelegramSettingsRequest(string? BotToken);

public sealed record SendTelegramTestRequest(string ChatId);
