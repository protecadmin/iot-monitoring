namespace Bms.Api.Dtos;

public sealed record DeviceBaselineDto(string ParameterKey, double Value, DateTimeOffset ReadingAt, DateTimeOffset SetAt);

/// <param name="ParameterKeys">Parameters to take a baseline for; null or empty takes every measured parameter in the latest reading.</param>
public sealed record SetDeviceBaselineRequest(IReadOnlyList<string>? ParameterKeys);
