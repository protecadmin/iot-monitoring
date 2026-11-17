using System.Text.Json.Nodes;
using Bms.Application.Common;

namespace Bms.UnitTests;

public class HubProtocolTests
{
    [Fact]
    public void Site_settings_require_an_IANA_timezone_and_nonblank_name()
    {
        var data = new JsonObject { ["name"] = "Project A", ["location"] = "", ["timezone"] = "Asia/Kolkata" };
        Assert.Equal("site.set", HubProtocol.Build("site.set", data, 1, "test")["op"]!.GetValue<string>());
        data["timezone"] = "India Standard Time";
        Assert.Throws<ArgumentException>(() => HubProtocol.Build("site.set", data, 1, "test"));
        data["timezone"] = "Asia/Kolkata"; data["name"] = "   ";
        Assert.Throws<ArgumentException>(() => HubProtocol.Build("site.set", data, 1, "test"));
    }

    [Fact]
    public void Get_uses_exact_v2_envelope_without_firmware_version_or_legacy_fields()
    {
        var packet = HubProtocol.Build("get", [], null, "cmd-test");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"v":2,"id":"cmd-test","op":"get"}"""), packet));
    }

    [Theory]
    [InlineData("ota.install")]
    [InlineData("network.prepare")]
    [InlineData("hub.restart_service")]
    [InlineData("display.reload")]
    public void Unsupported_operations_cannot_be_published(string op) => Assert.Throws<ArgumentException>(() => HubProtocol.Build(op, [], 1, "test"));

    [Fact]
    public void Display_requires_version_and_validates_zero_welcome_and_rotation_boundaries()
    {
        var data = JsonNode.Parse("""{"welcome_seconds":0,"rotation_seconds":5}""")!.AsObject();
        Assert.Throws<ArgumentException>(() => HubProtocol.Build("display.set", data, null, "test"));
        Assert.Equal(7, HubProtocol.Build("display.set", data, 7, "test")["if_version"]!.GetValue<int>());
        data["rotation_seconds"] = 181;
        Assert.Throws<ArgumentException>(() => HubProtocol.Build("display.set", data, 7, "test"));
    }

    [Fact]
    public void Threshold_warning_must_not_exceed_alarm()
    {
        var data = JsonNode.Parse("""{"api_alert_limits":{"noise":85,"ppv":0.5,"wbgt_warning":34,"wbgt":33}}""")!.AsObject();
        Assert.Throws<ArgumentException>(() => HubProtocol.Build("threshold.set", data, 1, "test"));
    }

    [Fact]
    public void Duplicate_sensor_tuples_and_unexpected_fields_are_rejected()
    {
        var data = JsonNode.Parse("""{"sensors":[{"source":"api","site":"A","sensor":"X"},{"source":"api","site":"A","sensor":"X"}]}""")!.AsObject();
        Assert.Throws<ArgumentException>(() => HubProtocol.Build("tv.select", data, 1, "test"));
        Assert.Throws<ArgumentException>(() => HubProtocol.Build("get", new JsonObject { ["shell"] = "reboot" }, null, "test"));
    }

    [Fact]
    public void Snapshot_verification_compares_saved_values_including_zero()
    {
        var snapshot = JsonNode.Parse("""{"tv":{"welcome_seconds":0,"rotation_seconds":15,"mode":"overview"}}""")!.AsObject();
        var data = new JsonObject { ["welcome_seconds"] = 0, ["rotation_seconds"] = 15 };
        Assert.True(HubProtocol.Matches(snapshot, "display.set", data));
        data["rotation_seconds"] = 20;
        Assert.False(HubProtocol.Matches(snapshot, "display.set", data));
    }

    [Fact]
    public void Captured_payloads_redact_nested_secrets_and_url_credentials()
    {
        var safe = HubProtocol.Redact("""{"network":{"password":"hidden","signal_percent":60},"api":{"url":"https://user:pass@example.com/readings?token=secret"},"items":[{"token":"secret"}]}""");
        Assert.DoesNotContain("hidden", safe);
        Assert.DoesNotContain("secret", safe);
        Assert.DoesNotContain("user:pass", safe);
        Assert.Contains("signal_percent", safe);
        Assert.Contains("https://example.com/readings", safe);
    }
}
