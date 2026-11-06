using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bms.Application.Abstractions.Telemetry;
using Bms.Application.Tilt;
using Bms.Domain.Entities;
using Bms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Bms.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class TiltMonitoringTests
{
    private sealed class Clock : TimeProvider { public DateTimeOffset Now=DateTimeOffset.Parse("2026-10-28T10:00:00Z"); public override DateTimeOffset GetUtcNow()=>Now; }
    private sealed class Reader : ITiltTelemetryReader
    {
        public List<TelemetryPoint> Points=[];
        public bool Fail;
        public Task<IReadOnlyList<TelemetryPoint>> GetTiltHistoryAsync(Guid id,DateTimeOffset from,DateTimeOffset to,CancellationToken ct)
        {
            if(Fail)throw new HttpRequestException("Simulated telemetry storage outage");
            return Task.FromResult<IReadOnlyList<TelemetryPoint>>(Points.Where(p=>p.Timestamp>=from&&p.Timestamp<to).ToList());
        }
    }
    private static TelemetryPoint P(string at,double x,double y)=>new(DateTimeOffset.Parse(at),new Dictionary<string,double>{{"inclX",x},{"inclY",y},{"temperature",31}});

    [Fact]
    public async Task PersistsDailyReferencesCalculatesBothDifferencesAndEnforcesScope()
    {
        var clock=new Clock();var reader=new Reader();
        using var root=new BmsApiFactory();
        await using var factory=root.WithWebHostBuilder(b=>b.ConfigureServices(services=>
        {
            services.RemoveAll<TimeProvider>();services.AddSingleton<TimeProvider>(clock);
            services.RemoveAll<ITiltTelemetryReader>();services.AddSingleton<ITiltTelemetryReader>(reader);
            foreach(var descriptor in services.Where(d=>d.ServiceType==typeof(IHostedService)&&d.ImplementationType==typeof(Bms.Api.Workers.TiltDailyWorker)).ToList())services.Remove(descriptor);
        }));
        var http=factory.CreateClient();
        var login=await http.PostAsJsonAsync("/api/auth/login",new{email="superadmin@bms.local",password="Passw0rd!"});
        login.EnsureSuccessStatusCode();
        var auth=await login.Content.ReadFromJsonAsync<Bms.Api.Dtos.LoginResponse>();
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",auth!.AccessToken);
        var suffix=Guid.NewGuid().ToString("N")[..10];
        async Task<T> Post<T>(string path,object body){var response=await http.PostAsJsonAsync(path,body);response.EnsureSuccessStatusCode();return (await response.Content.ReadFromJsonAsync<T>())!;}
        var type=await Post<Bms.Api.Dtos.DeviceTypeDto>("/api/device-types",new{name="Tilt test "+suffix,tiltAxes=new{x="inclX",y="inclY",z=(string?)null},supportsCommands=false});
        foreach(var key in new[]{"inclX","inclY"})await Post<Bms.Api.Dtos.DeviceTypeParameterDto>($"/api/device-types/{type.Id}/parameters",new{key,label=key,unit="°"});
        var tenant=await Post<Bms.Api.Dtos.TenantDto>("/api/tenants",new{name="Tilt test "+suffix,slug="tilt-"+suffix});
        var site=await Post<Bms.Api.Dtos.SiteDto>($"/api/sites?tenantId={tenant.Id}",new{code="site",name="Test site"});
        var device=await Post<Bms.Api.Dtos.DeviceDto>($"/api/devices?tenantId={tenant.Id}",new{siteId=site.Id,deviceTypeId=type.Id,deviceCode="tilt-"+suffix,name="Tilt test"});
        var endpoint=$"/api/devices/{device.Id}/tilt";
        Assert.Equal(HttpStatusCode.BadRequest,(await http.PutAsJsonAsync(endpoint,new{closingTime="25:00",timeZoneId="UTC"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await http.PutAsJsonAsync(endpoint,new{closingTime="23:59",timeZoneId="Invalid/Zone"})).StatusCode);
        (await http.PutAsJsonAsync(endpoint,new{closingTime="23:59",timeZoneId="UTC"})).EnsureSuccessStatusCode();
        reader.Points=[P("2026-10-28T10:01:00Z",3,4),P("2026-10-28T23:22:00Z",3.5,4.5),P("2026-10-29T23:22:00Z",2,4),P("2026-10-29T23:59:30Z",99,99),P("2026-10-31T23:22:00Z",4,5)];
        clock.Now=DateTimeOffset.Parse("2026-11-01T12:00:00Z");
        async Task Process(){using var scope=factory.Services.CreateScope();await scope.ServiceProvider.GetRequiredService<ITiltMonitoringService>().ProcessDeviceAsync(device.Id,default);}
        await Process();
        var summary=(await http.GetFromJsonAsync<TiltSummary>(endpoint+"?from=2026-10-28&to=2026-11-01"))!;
        Assert.Equal(new TiltValues(3,4,null),summary.Baseline!.Values);
        var day29=summary.Rows.Single(r=>r.Date==new DateOnly(2026,10,29));
        Assert.Equal(DateTimeOffset.Parse("2026-10-29T23:22:00Z"),day29.ReadingAt);
        Assert.Equal(new TiltValues(2,4,null),day29.Reading);
        Assert.Equal(new TiltValues(-1.5,-.5,null),day29.PreviousDayDifference);
        Assert.Equal(new TiltValues(-1,0,null),day29.OnboardingDifference);
        Assert.Equal("NoData",summary.Rows.Single(r=>r.Date==new DateOnly(2026,10,30)).Status);
        var day31=summary.Rows.Single(r=>r.Date==new DateOnly(2026,10,31));
        Assert.Equal(new TiltValues(null,null,null),day31.PreviousDayDifference);
        Assert.Equal(new TiltValues(1,1,null),day31.OnboardingDifference);
        Assert.Equal("Pending",summary.Rows.Single(r=>r.Date==new DateOnly(2026,11,1)).Status);
        reader.Points=[P("2026-10-28T10:01:00Z",999,999)];
        await Task.WhenAll(Process(),Process());
        var unchanged=(await http.GetFromJsonAsync<TiltSummary>(endpoint+"?from=2026-10-28&to=2026-11-01"))!;
        Assert.Equal(summary.Baseline,unchanged.Baseline);
        Assert.Equal(day29,unchanged.Rows.Single(r=>r.Date==day29.Date));
        (await http.PutAsJsonAsync(endpoint,new{closingTime="23:58",timeZoneId="UTC"})).EnsureSuccessStatusCode();
        Assert.Equal(summary.Baseline,(await http.GetFromJsonAsync<TiltSummary>(endpoint))!.Baseline);
        // A user from another tenant must not read or update this device's tilt settings.
        // Use the current supported CompanyAdmin seed account; legacy Viewer accounts are
        // intentionally blocked during login and are not suitable for a scope assertion.
        var otherTenant=factory.CreateClient();
        var otherTenantLogin=await otherTenant.PostAsJsonAsync("/api/auth/login",new{email="admin@whirlpool.example",password="Passw0rd!"});
        otherTenantLogin.EnsureSuccessStatusCode();
        var otherTenantAuth=(await otherTenantLogin.Content.ReadFromJsonAsync<Bms.Api.Dtos.LoginResponse>())!;
        otherTenant.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",otherTenantAuth.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,(await otherTenant.GetAsync(endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await otherTenant.PutAsJsonAsync(endpoint,new{closingTime="23:59",timeZoneId="UTC"})).StatusCode);

        // An outage must not freeze an empty day or a fabricated baseline.
        var outageDevice=await Post<Bms.Api.Dtos.DeviceDto>($"/api/devices?tenantId={tenant.Id}",new{siteId=site.Id,deviceTypeId=type.Id,deviceCode="tilt-outage-"+suffix,name="Tilt outage test"});
        clock.Now=DateTimeOffset.Parse("2026-10-28T10:00:00Z");
        (await http.PutAsJsonAsync($"/api/devices/{outageDevice.Id}/tilt",new{closingTime="23:59",timeZoneId="UTC"})).EnsureSuccessStatusCode();
        clock.Now=DateTimeOffset.Parse("2026-10-29T12:00:00Z");reader.Fail=true;
        using(var scope=factory.Services.CreateScope())
        {
            await Assert.ThrowsAsync<HttpRequestException>(()=>scope.ServiceProvider.GetRequiredService<ITiltMonitoringService>().ProcessDeviceAsync(outageDevice.Id,default));
            var db=scope.ServiceProvider.GetRequiredService<BmsDbContext>();db.ChangeTracker.Clear();
            Assert.False(await db.TiltDailyReadings.AnyAsync(r=>r.DeviceId==outageDevice.Id));
            Assert.Null((await db.TiltMonitoringSettings.SingleAsync(s=>s.DeviceId==outageDevice.Id)).BaselineAt);
        }
        reader.Fail=false;
    }
}
