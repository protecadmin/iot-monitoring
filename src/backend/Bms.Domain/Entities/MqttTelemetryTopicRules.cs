namespace Bms.Domain.Entities;

public static class MqttTelemetryTopicRules
{
    public const string GatewayPrefix = "protec/gateway/";
    public const string MeasurementSuffix = "/measurement";
    public static string DefaultFor(string gatewayId) => GatewayPrefix + gatewayId + MeasurementSuffix;

    public static string? GatewayIdFrom(string topic)
    {
        if (!topic.StartsWith(GatewayPrefix, StringComparison.Ordinal) || !topic.EndsWith(MeasurementSuffix, StringComparison.Ordinal)
            || topic.Length <= GatewayPrefix.Length + MeasurementSuffix.Length) return null;
        var id = topic[GatewayPrefix.Length..^MeasurementSuffix.Length];
        return ValidateGatewayId(id) is null ? id : null;
    }

    public static string? ValidateGatewayId(string gatewayId)
    {
        if (string.IsNullOrWhiteSpace(gatewayId) || gatewayId.IndexOfAny(['/', '\0', '+', '#']) >= 0)
            return "Gateway ID must be one MQTT topic segment, without /, +, # or null characters.";
        return Validate(DefaultFor(gatewayId));
    }

    public static string? Validate(string? topic)
    {
        if (string.IsNullOrEmpty(topic)) return null;
        if (topic.Length > 512) return "MQTT telemetry topic must be at most 512 characters.";
        if (topic.IndexOfAny(['\0', '+', '#']) >= 0)
            return "Enter an exact MQTT publish topic, without null characters or subscription wildcards (+ or #).";
        if (topic == "bms" || topic.StartsWith("bms/", StringComparison.Ordinal) || topic == "$SYS" || topic.StartsWith("$SYS/", StringComparison.Ordinal))
            return "This topic is reserved for internal service messages.";
        return null;
    }
}
