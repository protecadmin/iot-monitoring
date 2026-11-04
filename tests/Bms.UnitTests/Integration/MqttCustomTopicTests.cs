using System.Text;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Transports;
using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Protocols.Protocols;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Bms.UnitTests.Integration;

public class MqttCustomTopicTests
{
    [Theory]
    [InlineData("machine01")]
    [InlineData("factory/temperature")]
    [InlineData("sensors/aa/site01/device01/data")]
    [InlineData("vendor/room/floor/machine/measurements/live")]
    [InlineData("/factory//sensor/")]
    [InlineData("$vendor/measurements")]
    [InlineData("工場/温度")]
    public async Task Custom_topic_routes_to_registered_device_and_preserves_payload(string topic)
    {
        var device = new Device { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), SiteId = Guid.NewGuid(), PayloadFormat = PayloadFormat.Json };
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(x => x.GetByMqttTopicAsync(topic, It.IsAny<CancellationToken>())).ReturnsAsync(device);
        var handler = new MqttProtocolHandler(devices.Object, NullLogger<MqttProtocolHandler>.Instance);
        var payload = Encoding.UTF8.GetBytes("{\"temperature\":31.1}");
        var received = DateTimeOffset.UtcNow;

        var result = await handler.TryBuildRawMessageAsync(new TransportMessage(TransportType.Mqtt, topic, payload, received), default);

        Assert.NotNull(result);
        Assert.Equal(device.Id, result.DeviceId);
        Assert.Equal(device.TenantId, result.TenantId);
        Assert.Equal(device.SiteId, result.SiteId);
        Assert.Equal(topic, result.Topic);
        Assert.Equal(payload, result.RawPayload);
        Assert.Equal(received, result.ReceivedAt);
        devices.Verify(x => x.GetByCodesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Legacy_topic_still_resolves_by_company_site_and_device()
    {
        var devices = new Mock<IDeviceRepository>();
        var device = new Device { Id = Guid.NewGuid() };
        devices.Setup(x => x.GetByCodesAsync("aa", "site01", "device01", It.IsAny<CancellationToken>())).ReturnsAsync(device);
        var handler = new MqttProtocolHandler(devices.Object, NullLogger<MqttProtocolHandler>.Instance);
        var result = await handler.TryBuildRawMessageAsync(new TransportMessage(TransportType.Mqtt, "iot/aa/site01/device01/telemetry", [], DateTimeOffset.UtcNow), default);
        Assert.Equal(device.Id, result?.DeviceId);
    }

    [Fact]
    public async Task Unregistered_topic_cannot_guess_a_device()
    {
        var devices = new Mock<IDeviceRepository>();
        var handler = new MqttProtocolHandler(devices.Object, NullLogger<MqttProtocolHandler>.Instance);
        Assert.Null(await handler.TryBuildRawMessageAsync(new TransportMessage(TransportType.Mqtt, "random/topic", [], DateTimeOffset.UtcNow), default));
    }

    [Theory]
    [InlineData("machine01")]
    [InlineData("a/b/c/d/e/f/g")]
    [InlineData("sensors/aa/site01/device01/data")]
    [InlineData("/a//b/")]
    [InlineData("$vendor/topic")]
    [InlineData(null)]
    [InlineData("")]
    public void Valid_publish_names_have_no_required_layout(string? topic) => Assert.Null(MqttTelemetryTopicRules.Validate(topic));

    [Theory]
    [InlineData("factory/+")]
    [InlineData("#")]
    [InlineData("factory/\0/data")]
    [InlineData("bms/realtime/events")]
    [InlineData("$SYS/broker/uptime")]
    public void Wildcards_and_internal_topics_are_not_device_publish_names(string topic) => Assert.NotNull(MqttTelemetryTopicRules.Validate(topic));
}
