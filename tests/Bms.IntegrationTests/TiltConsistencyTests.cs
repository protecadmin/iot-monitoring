using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bms.Application.Abstractions.Telemetry;
using Bms.Application.Tilt;
using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Bms.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class TiltConsistencyTests
{
    private sealed class Clock : TimeProvider { public DateTimeOffset Now=DateTimeOffset.Parse("2026-10-28T20:00:00Z");public override DateTimeOffset GetUtcNow()=>Now; }
    private sealed class Reader : ITiltTelemetryReader
    {
        public List<TelemetryPoint> Points=[P("2026-10-28T20:01:00Z",3,4),P("2026-10-28T23:22:00Z",2,4)];
        public TaskCompletionSource? Entered,Resume;
        public async Task<IReadOnlyList<TelemetryPoint>> GetTiltHistoryAsync(Guid id,DateTimeOffset from,DateTimeOffset to,CancellationToken ct)
        { if(Entered is not null){Entered.TrySetResult();await Resume!.Task.WaitAsync(ct);}return Points.Where(p=>p.Timestamp>=from&&p.Timestamp<to).ToList(); }
    }
    private static TelemetryPoint P(string at,double x,double y)=>new(DateTimeOffset.Parse(at),new Dictionary<string,double>{{"inclX",x},{"inclY",y}});
    private sealed class Harness : IDisposable
    {
        public readonly Clock Clock=new();public readonly Reader Reader=new();public readonly BmsApiFactory Root=new();
        public WebApplicationFactory<Bms.Api.ApiMarker> Factory=null!;public HttpClient Http=null!;
        public Bms.Api.Dtos.DeviceDto Device=null!;public Bms.Api.Dtos.DeviceTypeDto Type=null!;public Guid XParameterId;
        public string Endpoint=>$"/api/devices/{Device.Id}/tilt";
        public async Task<T> Post<T>(string path,object body){var r=await Http.PostAsJsonAsync(path,body);r.EnsureSuccessStatusCode();return (await r.Content.ReadFromJsonAsync<T>())!;}
        public async Task Process(){using var scope=Factory.Services.CreateScope();await scope.ServiceProvider.GetRequiredService<ITiltMonitoringService>().ProcessDeviceAsync(Device.Id,default);}
        public async Task<TiltSummary> Summary()=> (await Http.GetFromJsonAsync<TiltSummary>(Endpoint+"?from=2026-10-28&to=2026-10-29"))!;
        public static async Task<Harness> Create()
        {
            var h=new Harness();h.Factory=h.Root.WithWebHostBuilder(b=>b.ConfigureServices(s=>{
                s.RemoveAll<TimeProvider>();s.AddSingleton<TimeProvider>(h.Clock);s.RemoveAll<ITiltTelemetryReader>();s.AddSingleton<ITiltTelemetryReader>(h.Reader);
                foreach(var d in s.Where(d=>d.ServiceType==typeof(IHostedService)&&d.ImplementationType==typeof(Bms.Api.Workers.TiltDailyWorker)).ToList())s.Remove(d);
            }));h.Http=h.Factory.CreateClient();
            var auth=await h.Post<Bms.Api.Dtos.LoginResponse>("/api/auth/login",new{email="superadmin@bms.local",password="Passw0rd!"});h.Http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",auth.AccessToken);
            var suffix=Guid.NewGuid().ToString("N")[..10];
            h.Type=await h.Post<Bms.Api.Dtos.DeviceTypeDto>("/api/device-types",new{name="Tilt consistency "+suffix,tiltAxes=new{x="inclX",y="inclY",z=(string?)null}});
            foreach(var key in new[]{"inclX","inclY"}){var parameter=await h.Post<Bms.Api.Dtos.DeviceTypeParameterDto>($"/api/device-types/{h.Type.Id}/parameters",new{key,label=key,unit="°"});if(key=="inclX")h.XParameterId=parameter.Id;}
            var tenant=await h.Post<Bms.Api.Dtos.TenantDto>("/api/tenants",new{name="Consistency "+suffix,slug="consistency-"+suffix});
            var site=await h.Post<Bms.Api.Dtos.SiteDto>($"/api/sites?tenantId={tenant.Id}",new{code="site",name="Test site"});
            h.Device=await h.Post<Bms.Api.Dtos.DeviceDto>($"/api/devices?tenantId={tenant.Id}",new{siteId=site.Id,deviceTypeId=h.Type.Id,deviceCode="consistency-"+suffix,name="Tilt consistency"});
            (await h.Http.PutAsJsonAsync(h.Endpoint,new{closingTime="23:59",timeZoneId="UTC"})).EnsureSuccessStatusCode();
            h.Clock.Now=DateTimeOffset.Parse("2026-10-29T12:00:00Z");return h;
        }
        public void Dispose(){Http?.Dispose();Factory?.Dispose();Root.Dispose();}
    }

    [Fact]
    public async Task EarlierCutoffDoesNotHideAnAlreadySavedOnboardingDay()
    {
        using var h=await Harness.Create();await h.Process();
        (await h.Http.PutAsJsonAsync(h.Endpoint,new{closingTime="09:00",timeZoneId="UTC"})).EnsureSuccessStatusCode();
        var saved=Assert.Single((await h.Summary()).Rows.Where(r=>r.Date==new DateOnly(2026,10,28)));
        Assert.Equal(2,saved.Reading.X);Assert.Equal(DateTimeOffset.Parse("2026-10-28T23:59:00Z"),saved.CutoffAt);
    }

    [Fact]
    public async Task PendingTimeSeriesBackfillDelaysBaselineAndDailyFinalization()
    {
        using var h=await Harness.Create();h.Reader.Points=[P("2026-10-28T23:22:00Z",2,4)];
        Guid pendingId;
        using(var scope=h.Factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<BmsDbContext>();var raw=new RawDeviceMessage{DeviceId=h.Device.Id,TenantId=h.Device.TenantId,SiteId=h.Device.SiteId,ProcessingStatus=MessageProcessingStatus.Persisted,ReceivedAt=DateTimeOffset.Parse("2026-10-28T20:01:00Z"),DeviceTimestamp=DateTimeOffset.Parse("2026-10-28T20:01:00Z")};pendingId=raw.Id;db.RawDeviceMessages.Add(raw);await db.SaveChangesAsync();
        }
        await h.Process();var pending=await h.Summary();Assert.Null(pending.Baseline);Assert.All(pending.Rows,r=>Assert.Equal("Pending",r.Status));
        using(var scope=h.Factory.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<BmsDbContext>();var raw=await db.RawDeviceMessages.FindAsync(pendingId);raw!.TimeSeriesWrittenAt=h.Clock.Now;await db.SaveChangesAsync();}
        h.Reader.Points.Insert(0,P("2026-10-28T20:01:00Z",3,4));await h.Process();var recovered=await h.Summary();Assert.Equal(3,recovered.Baseline!.Values.X);Assert.Equal("Complete",recovered.Rows.Single(r=>r.Date==new DateOnly(2026,10,28)).Status);
    }

    [Fact]
    public async Task AddingPreviouslyAbsentZPreservesTheSavedReferenceAndHistory()
    {
        using var h=await Harness.Create(); await h.Process(); var before=await h.Summary();
        await h.Post<Bms.Api.Dtos.DeviceTypeParameterDto>($"/api/device-types/{h.Type.Id}/parameters",new{key="inclZ",label="Z inclination",unit="degrees"});
        (await h.Http.PutAsJsonAsync($"/api/device-types/{h.Type.Id}",new{name=h.Type.Name,tiltAxes=new{x="inclX",y="inclY",z="inclZ"}})).EnsureSuccessStatusCode();
        var after=await h.Summary();Assert.Equal("inclZ",after.Axes.Z);Assert.Equal(before.Baseline,after.Baseline);
        Assert.Equal(before.Rows.Single(r=>r.Status=="Complete"),after.Rows.Single(r=>r.Status=="Complete"));
        Assert.Null(after.Baseline!.Values.Z);
        Assert.Equal(HttpStatusCode.Conflict,(await h.Http.PutAsJsonAsync($"/api/device-types/{h.Type.Id}",new{name=h.Type.Name,tiltAxes=new{x="inclX",y="inclY",z="differentZ"}})).StatusCode);
    }

    [Fact]
    public async Task TiltAxisMeaningCannotBeRemappedDeletedOrMadeDerived()
    {
        using var h=await Harness.Create();
        Assert.Equal(HttpStatusCode.Conflict,(await h.Http.PutAsJsonAsync($"/api/device-types/{h.Type.Id}",new{name=h.Type.Name,tiltAxes=new{x="inclY",y="inclX",z=(string?)null}})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await h.Http.DeleteAsync($"/api/device-types/{h.Type.Id}/parameters/{h.XParameterId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await h.Http.PutAsJsonAsync($"/api/device-types/{h.Type.Id}/parameters/{h.XParameterId}",new{label="X",unit="°",kind="Derived"})).StatusCode);
    }

    [Fact]
    public async Task ScheduleUpdatesWaitForAnInProgressFinalization()
    {
        using var h=await Harness.Create();h.Reader.Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);h.Reader.Resume=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var processing=h.Process();await h.Reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var update=h.Http.PutAsJsonAsync(h.Endpoint,new{closingTime="09:00",timeZoneId="UTC"});await Task.Delay(200);
        var waited=!update.IsCompleted;h.Reader.Resume.SetResult();await processing;(await update).EnsureSuccessStatusCode();
        Assert.True(waited,"A settings update must acquire the same device lock as finalization.");
    }
}
