using Bms.Application.Tilt;
using Bms.Domain.Entities;
using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Telemetry;

public sealed class TiltMonitoringService(BmsDbContext db,ITiltTelemetryReader telemetry,TimeProvider clock) : ITiltMonitoringService
{
    public async Task ConfigureAsync(Device device,TimeOnly closingTime,string timeZoneId,CancellationToken ct)
    {
        _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var type=await db.DeviceTypes.AsNoTracking().Include(t=>t.Parameters).SingleAsync(t=>t.Id==device.DeviceTypeId,ct);
        if(type.TiltAxes is null)
            throw new ArgumentException("Daily tilt monitoring is not enabled for this device type.");
        var keys=new[]{type.TiltAxes.X,type.TiltAxes.Y}.Concat(type.TiltAxes.Z is null?[]:new[]{type.TiltAxes.Z});
        if(keys.Any(key=>!type.Parameters.Any(p=>p.Key==key && p.Kind!=Bms.Domain.ParameterKind.Derived)))
            throw new ArgumentException("Configure the device type's measured tilt parameters first.");
        var now=clock.GetUtcNow(); var id=Guid.NewGuid();
        var strategy=db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async()=>
        {
            await using var tx=await db.Database.BeginTransactionAsync(ct);
            // Use the finalizer's device lock so edits cannot race with a snapshot.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({device.Id.ToString()}, 0))",ct);
            // Atomic create/update preserves the original onboarding reference and start time.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "TiltMonitoringSettings" ("Id","TenantId","DeviceId","ClosingTime","TimeZoneId","OnboardedAt","CreatedAt","UpdatedAt")
            VALUES ({id},{device.TenantId},{device.Id},{closingTime},{timeZoneId},{now},{now},{now})
            ON CONFLICT ("DeviceId") DO UPDATE SET "ClosingTime"=EXCLUDED."ClosingTime", "TimeZoneId"=EXCLUDED."TimeZoneId", "UpdatedAt"=EXCLUDED."UpdatedAt"
            """,ct);
            await tx.CommitAsync(ct);
        });
    }

    public async Task ProcessDeviceAsync(Guid deviceId,CancellationToken ct)
    {
        var strategy=db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async()=>
        {
            db.ChangeTracker.Clear();
            await using var tx=await db.Database.BeginTransactionAsync(ct);
            // Worker and API catch-up cannot race to save different daily snapshots.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({deviceId.ToString()}, 0))",ct);
            var settings=await db.TiltMonitoringSettings.SingleOrDefaultAsync(s=>s.DeviceId==deviceId,ct);
            if(settings is null) { await tx.CommitAsync(ct);return; }
            var axes=await db.Devices.AsNoTracking().Where(d=>d.Id==deviceId).Join(db.DeviceTypes,d=>d.DeviceTypeId,t=>t.Id,(d,t)=>t.TiltAxes).SingleOrDefaultAsync(ct);
            if(axes is null) { await tx.CommitAsync(ct);return; }
            var now=clock.GetUtcNow();
            // A successful Influx query can still be incomplete while SQL's
            // durable ingestion outbox is waiting for a time-series write.
            // Retry after backfill rather than freeze an incomplete reference.
            var pending=await db.RawDeviceMessages.AsNoTracking().AnyAsync(r=>r.DeviceId==deviceId
                && r.ProcessingStatus==Bms.Domain.MessageProcessingStatus.Persisted && r.TimeSeriesWrittenAt==null
                && (r.DeviceTimestamp??r.ReceivedAt)>=settings.OnboardedAt
                && (r.DeviceTimestamp??r.ReceivedAt)<=now,ct);
            if(pending) { await tx.CommitAsync(ct);return; }
            var zone=TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
            if(settings.BaselineAt is null)
            {
                var history=await telemetry.GetTiltHistoryAsync(deviceId,settings.OnboardedAt,now.AddMilliseconds(1),ct);
                var first=TiltDailyCalculator.Select(history,axes,settings.OnboardedAt,now,first:true);
                if(first is not null)
                {
                    settings.BaselineAt=first.ReadingAt;settings.BaselineX=first.X;settings.BaselineY=first.Y;settings.BaselineZ=first.Z;
                }
            }
            var firstDate=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(settings.OnboardedAt,zone).DateTime);
            var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,zone).DateTime);
            var lastDate=await db.TiltDailyReadings.Where(r=>r.DeviceId==deviceId).Select(r=>(DateOnly?)r.Date).MaxAsync(ct);
            var start=lastDate?.AddDays(1) ?? firstDate;
            // Bounded catch-up after downtime; subsequent passes continue the backlog.
            for(var date=start;date<=today && date<start.AddDays(31);date=date.AddDays(1))
            {
                var cutoff=TiltDailyCalculator.AtLocal(date,settings.ClosingTime,settings.TimeZoneId);
                if(cutoff>now)break;
                if(cutoff<settings.OnboardedAt)continue;
                var dayStart=TiltDailyCalculator.AtLocal(date,TimeOnly.MinValue,settings.TimeZoneId);
                var from=dayStart>settings.OnboardedAt?dayStart:settings.OnboardedAt;
                // Strict reader throws on outages. No false 'No data' row is committed.
                var history=await telemetry.GetTiltHistoryAsync(deviceId,from,cutoff.AddMilliseconds(1),ct);
                var reading=TiltDailyCalculator.Select(history,axes,from,cutoff);
                db.TiltDailyReadings.Add(new TiltDailyReading { DeviceId=deviceId,TenantId=settings.TenantId,Date=date,CutoffAt=cutoff,ReadingAt=reading?.ReadingAt,X=reading?.X,Y=reading?.Y,Z=reading?.Z });
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    public async Task<TiltSummary> GetSummaryAsync(Device device,DateOnly? from,DateOnly? to,CancellationToken ct)
    {
        var axes=await db.DeviceTypes.AsNoTracking().Where(t=>t.Id==device.DeviceTypeId).Select(t=>t.TiltAxes).SingleOrDefaultAsync(ct)
            ?? throw new ArgumentException("This device type has no daily tilt capability.");
        var settings=await db.TiltMonitoringSettings.AsNoTracking().SingleOrDefaultAsync(s=>s.DeviceId==device.Id,ct);
        if(settings is null)return new(axes,null,null,[]);
        var zone=TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),zone).DateTime);
        var end=to??today;var start=from??end.AddDays(-29);
        if(start.Year<1970 || end.Year>9998 || end<start || end.DayNumber-start.DayNumber>365)throw new ArgumentException("Choose a valid date range of at most 366 days.");
        var stored=await db.TiltDailyReadings.AsNoTracking().Where(r=>r.DeviceId==device.Id && r.Date>=start.AddDays(-1) && r.Date<=end).ToDictionaryAsync(r=>r.Date,ct);
        var rows=new List<TiltDailyRow>();
        for(var date=start;date<=end;date=date.AddDays(1))
        {
            var cutoff=TiltDailyCalculator.AtLocal(date,settings.ClosingTime,settings.TimeZoneId);
            stored.TryGetValue(date,out var row);stored.TryGetValue(date.AddDays(-1),out var previous);
            // Saved dates retain their original cutoff even after schedule edits.
            if(row is null && (cutoff<settings.OnboardedAt || date>today))continue;
            var values=new TiltValues(row?.X,row?.Y,row?.Z);
            var reference=previous?.ReadingAt is null?null:new TiltValues(previous.X,previous.Y,previous.Z);
            var baseline=settings.BaselineAt is not null && settings.BaselineAt<=row?.ReadingAt?new TiltValues(settings.BaselineX,settings.BaselineY,settings.BaselineZ):null;
            rows.Add(new(date,row?.CutoffAt??cutoff,row?.ReadingAt,row is null?"Pending":row.ReadingAt is null?"NoData":"Complete",values,TiltDailyCalculator.Difference(values,reference),TiltDailyCalculator.Difference(values,baseline)));
        }
        return new(axes,new(settings.ClosingTime.ToString("HH:mm"),settings.TimeZoneId,settings.OnboardedAt),settings.BaselineAt is {} at?new(at,new(settings.BaselineX,settings.BaselineY,settings.BaselineZ)):null,rows.OrderByDescending(r=>r.Date).ToList());
    }
}
