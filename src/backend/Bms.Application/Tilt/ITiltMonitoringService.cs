using Bms.Domain.Entities;

namespace Bms.Application.Tilt;

public interface ITiltTelemetryReader
{
    Task<IReadOnlyList<Bms.Application.Abstractions.Telemetry.TelemetryPoint>> GetTiltHistoryAsync(Guid deviceId,DateTimeOffset from,DateTimeOffset to,CancellationToken ct);
}
public sealed record TiltSettingsView(string ClosingTime,string TimeZoneId,DateTimeOffset OnboardedAt);
public sealed record TiltBaselineView(DateTimeOffset ReadingAt,TiltValues Values);
public sealed record TiltDailyRow(DateOnly Date,DateTimeOffset CutoffAt,DateTimeOffset? ReadingAt,string Status,TiltValues Reading,TiltValues PreviousDayDifference,TiltValues OnboardingDifference);
public sealed record TiltSummary(TiltAxes Axes,TiltSettingsView? Settings,TiltBaselineView? Baseline,IReadOnlyList<TiltDailyRow> Rows);
public interface ITiltMonitoringService
{
    Task ConfigureAsync(Device device,TimeOnly closingTime,string timeZoneId,CancellationToken ct);
    Task ProcessDeviceAsync(Guid deviceId,CancellationToken ct);
    Task<TiltSummary> GetSummaryAsync(Device device,DateOnly? from,DateOnly? to,CancellationToken ct);
}
