using Bms.Protocols.Transports;

namespace Bms.UnitTests.Integration;

public class MqttTopicFiltersTests
{
    [Theory]
    // The ingestion worker's real overlap: one message matched three subscriptions.
    [InlineData("protec/gateway/+/+", "protec/gateway/+/measurement")]
    [InlineData("protec/gateway/+/+", "protec/gateway/860738070588839/measurement")]
    [InlineData("protec/gateway/+/measurement", "protec/gateway/860738070588839/measurement")]
    [InlineData("protec/gateway/+/measurement", "protec/gateway/+/measurement")]
    [InlineData("protec/hubs/#", "protec/hubs/hub-1/heartbeat")]
    [InlineData("protec/hubs/#", "protec/hubs/+/ack")]
    [InlineData("protec/hubs/#", "protec/hubs/#")]
    [InlineData("protec/hubs/#", "protec/hubs")] // '#' also matches the parent level
    [InlineData("#", "protec/gateway/x/status")]
    [InlineData("+/+", "a/b")]
    public void Broader_filter_covers_narrower(string broader, string narrower) =>
        Assert.True(MqttTopicFilters.Covers(broader, narrower));

    [Theory]
    [InlineData("protec/gateway/+/measurement", "protec/gateway/+/+")]
    [InlineData("protec/gateway/+/measurement", "protec/gateway/x/health")]
    [InlineData("protec/gateway/860738070588839/measurement", "protec/gateway/+/measurement")]
    [InlineData("protec/gateway/+/+", "protec/hubs/hub-1/heartbeat")]
    [InlineData("protec/gateway/+/+", "protec/gateway/x/y/z")] // '+' is exactly one level
    [InlineData("protec/gateway/+/+", "protec/gateway/x")]
    [InlineData("protec/gateway/+/+", "protec/gateway/#")]
    [InlineData("protec/hubs/+", "protec/hubs/#")]
    [InlineData("#", "$SYS/broker/uptime")] // leading wildcards never match $-topics
    [InlineData("+/broker/uptime", "$SYS/broker/uptime")]
    public void Filter_does_not_cover_topics_it_cannot_match(string broader, string narrower) =>
        Assert.False(MqttTopicFilters.Covers(broader, narrower));
}
