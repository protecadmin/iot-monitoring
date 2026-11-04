using Bms.Domain.Entities;

namespace Bms.UnitTests.Integration;

public class ProtecGatewayTopicTests
{
    [Theory]
    [InlineData("860738070588839")]
    [InlineData("gateway-01")]
    public void Default_topic_contains_gateway_id(string gatewayId)
    {
        var topic = MqttTelemetryTopicRules.DefaultFor(gatewayId);
        Assert.Equal($"protec/gateway/{gatewayId}/measurement", topic);
        Assert.Equal(gatewayId, MqttTelemetryTopicRules.GatewayIdFrom(topic));
        Assert.Null(MqttTelemetryTopicRules.ValidateGatewayId(gatewayId));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("gateway/01")]
    [InlineData("+")]
    [InlineData("#")]
    [InlineData("gateway\0id")]
    public void Gateway_id_is_a_real_topic_segment(string gatewayId) => Assert.NotNull(MqttTelemetryTopicRules.ValidateGatewayId(gatewayId));

    [Theory]
    [InlineData("protec/gateway//measurement")]
    [InlineData("protec/gateway/a/b/measurement")]
    [InlineData("protec/gateway/+/measurement")]
    [InlineData("protec/gateway/123/status")]
    [InlineData("vendor/custom/topic")]
    public void Other_topics_are_not_interpreted_as_gateway_defaults(string topic) => Assert.Null(MqttTelemetryTopicRules.GatewayIdFrom(topic));
}
