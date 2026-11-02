using Bms.Application.Abstractions.Telemetry;
using Bms.Domain.Entities;

namespace Bms.Application.Tilt;

public sealed record TiltValues(double? X, double? Y, double? Z);
public sealed record TiltReading(DateTimeOffset ReadingAt, double X, double Y, double? Z)
{
    public TiltValues Values => new(X,Y,Z);
}

public static class TiltDailyCalculator
{
    public static DateTimeOffset AtLocal(DateOnly date, TimeOnly time, string zoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        // A nonexistent wall-clock cutoff closes at the first valid minute.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Min() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local,offset).ToUniversalTime();
    }

    public static TiltReading? Select(IEnumerable<TelemetryPoint> points, TiltAxes axes, DateTimeOffset from, DateTimeOffset cutoff, bool first = false)
    {
        var ordered = points.Where(p=>p.Timestamp>=from && p.Timestamp<=cutoff);
        ordered = first ? ordered.OrderBy(p=>p.Timestamp) : ordered.OrderByDescending(p=>p.Timestamp);
        foreach (var point in ordered)
        {
            if (!Value(point,axes.X,out var x) || !Value(point,axes.Y,out var y)) continue;
            double? z = null;
            if (axes.Z is { Length: >0 }) { if(!Value(point,axes.Z,out var zValue)) continue; z=zValue; }
            return new TiltReading(point.Timestamp,x,y,z);
        }
        return null;
    }

    private static bool Value(TelemetryPoint point,string key,out double value) => point.Fields.TryGetValue(key,out value) && double.IsFinite(value);
    public static TiltValues Difference(TiltValues current,TiltValues? reference) => new(Sub(current.X,reference?.X),Sub(current.Y,reference?.Y),Sub(current.Z,reference?.Z));
    private static double? Sub(double? a,double? b) => a.HasValue && b.HasValue ? a.Value-b.Value : null;
}
