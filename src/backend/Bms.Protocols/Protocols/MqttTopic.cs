namespace Bms.Protocols.Protocols;

/// <summary>
/// Parses the <c>iot/{tenantSlug}/{siteCode}/{deviceCode}/{messageType}</c>
/// topic shape (see docs/mqtt/topic-design.md). Deliberately does not
/// carry payload format — that is resolved from the device registry.
/// </summary>
public sealed record MqttTopic(string TenantSlug, string SiteCode, string DeviceCode, string MessageType)
{
    public static bool TryParse(string topic, out MqttTopic? parsed)
    {
        var segments = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 5 || segments[0] != "iot")
        {
            parsed = null;
            return false;
        }

        var messageType = string.Join('/', segments.Skip(4));
        parsed = new MqttTopic(segments[1], segments[2], segments[3], messageType);
        return true;
    }
}
