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
        if (segments.Length < 4 || segments[0] != "protec" || segments[1] != "gateway")
        {
            parsed = null;
            return false;
        }

        var messageType = string.Join('/', segments.Skip(3));
        parsed = new MqttTopic("", "", segments[2], messageType);
        return true;
    }
}
