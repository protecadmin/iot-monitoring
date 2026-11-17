using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bms.Application.Common;

/// <summary>The installed V2 RC1 contract. Proposed OTA/network operations are deliberately excluded.</summary>
public static class HubProtocol
{
    public static string Redact(string json)
    {
        var node = JsonNode.Parse(json);
        void Visit(JsonNode? value)
        {
            if (value is JsonArray array) { foreach (var item in array) Visit(item); }
            if (value is not JsonObject obj) return;
            foreach (var (key, child) in obj.ToList())
            {
                var normalized = key.ToLowerInvariant();
                if (new[] { "password", "token", "secret", "private_key", "certificate", "credential", "base64", "pin" }.Any(normalized.Contains)) obj[key] = "[redacted]";
                else if (child is JsonValue val && val.TryGetValue<string>(out var str) && Uri.TryCreate(str, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                    obj[key] = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.GetLeftPart(UriPartial.Path);
                else Visit(child);
            }
        }
        Visit(node);
        return node!.ToJsonString();
    }

    public static bool IsReadOnly(string op) => op is "get" or "status.get" or "api.test";

    public static JsonObject Build(string op, JsonObject data, int? version, string id)
    {
        void Require(bool ok, string message) { if (!ok) throw new ArgumentException(message); }
        string Text(string key) => data[key]?.GetValue<string>() ?? "";
        double Number(JsonObject obj, string key) => obj[key] is JsonValue val && val.GetValueKind() == JsonValueKind.Number
            && double.TryParse(val.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : double.NaN;
        void Range(string key, double min, double max) => Require(Number(data, key) >= min && Number(data, key) <= max && Number(data, key) % 1 == 0, $"{key} must be an integer from {min} to {max}.");
        string[] keys = op switch
        {
            "get" or "status.get" or "api.refresh" => [],
            "site.set" => ["name", "location", "timezone"],
            "tv.select" => ["sensors"],
            "api.test" => ["url"],
            "api.set" => ["url", "interval_s"],
            "display.set" => ["welcome_seconds", "rotation_seconds"],
            "threshold.set" => ["api_alert_limits"],
            "logo.begin" => ["transfer", "mime", "bytes", "sha256", "chunks"],
            "logo.chunk" => ["transfer", "index", "base64"],
            "logo.commit" => ["transfer"],
            _ => throw new ArgumentException("Operation is not supported by the installed RC1 contract.")
        };
        Require(data.All(p => keys.Contains(p.Key)) && keys.All(data.ContainsKey), "Unexpected or missing operation fields.");
        if (!IsReadOnly(op)) Require(version >= 0, "A current snapshot config_version is required.");
        switch (op)
        {
            case "site.set":
                Require(!string.IsNullOrWhiteSpace(Text("name")) && Text("name").Length <= 200 && Text("location").Length <= 500, "Invalid site name or location.");
                Require(TimeZoneInfo.TryConvertIanaIdToWindowsId(Text("timezone"), out _) && TimeZoneInfo.TryFindSystemTimeZoneById(Text("timezone"), out _), "Use a valid IANA timezone.");
                break;
            case "api.test": case "api.set":
                Require(Uri.TryCreate(Text("url"), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo == "" && uri.Query == "" && uri.Fragment == "", "Use an approved HTTP(S) endpoint without credentials or query tokens. The HUB enforces its host allowlist.");
                if (op == "api.set") Range("interval_s", 5, 3600);
                break;
            case "display.set": Range("welcome_seconds", 0, 60); Range("rotation_seconds", 5, 180); break;
            case "threshold.set":
                Require(data["api_alert_limits"] is JsonObject, "API alert limits are required.");
                var limits = (JsonObject)data["api_alert_limits"]!;
                Require(limits.Count == 4 && new[] { "noise", "ppv", "wbgt_warning", "wbgt" }.All(k => double.IsFinite(Number(limits, k)) && Number(limits, k) >= 0), "Four finite, nonnegative API alert limits are required.");
                Require(Number(limits, "wbgt_warning") <= Number(limits, "wbgt"), "WBGT warning must not exceed the high alarm limit.");
                break;
            case "tv.select":
                Require(data["sensors"] is JsonArray, "Sensor selection must be an array.");
                var sensors = (JsonArray)data["sensors"]!;
                Require(sensors.Count <= 100, "Too many selected sensors.");
                var tuples = new HashSet<string>();
                foreach (var s in sensors)
                {
                    Require(s is JsonObject obj && obj.Count == 3 && obj["source"]?.GetValue<string>() is "api" or "mqtt" && !string.IsNullOrWhiteSpace(obj["site"]?.GetValue<string>()) && !string.IsNullOrWhiteSpace(obj["sensor"]?.GetValue<string>()), "Use exact source, site and sensor tuples from inventory.");
                    Require(tuples.Add($"{s!["source"]}\n{s["site"]}\n{s["sensor"]}"), "Duplicate sensor selection.");
                }
                break;
            case "logo.begin":
                Require(Text("mime") is "image/png" or "image/jpeg" or "image/webp", "PNG, JPEG or WebP required.");
                Range("bytes", 1, 262144); Range("chunks", 1, 22);
                Require(System.Text.RegularExpressions.Regex.IsMatch(Text("sha256"), "^[a-fA-F0-9]{64}$"), "Invalid SHA-256.");
                break;
            case "logo.chunk":
                Range("index", 0, 21);
                Require(Convert.FromBase64String(Text("base64")).Length is > 0 and <= 12288, "Logo chunks must contain at most 12,288 decoded bytes.");
                break;
        }
        if (op.StartsWith("logo.")) Require(Text("transfer").Length is > 0 and <= 100, "Invalid logo transfer ID.");
        var packet = new JsonObject { ["v"] = 2, ["id"] = id, ["op"] = op };
        if (!IsReadOnly(op)) packet["if_version"] = version;
        if (data.Count > 0 || op == "api.refresh") packet["data"] = data.DeepClone();
        return packet;
    }

    public static bool Matches(JsonObject snapshot, string op, JsonObject data) => op switch
    {
        "site.set" => Contains(snapshot["site"], data),
        "display.set" => Contains(snapshot["tv"], data),
        "api.set" => Contains(snapshot["api"], data),
        "threshold.set" => Contains(snapshot["thresholds"], data),
        "tv.select" => JsonNode.DeepEquals(snapshot["tv"]?["selected_sensors"], data["sensors"]),
        _ => false
    };

    private static bool Contains(JsonNode? actual, JsonObject expected) => actual is JsonObject obj && expected.All(p => JsonNode.DeepEquals(obj[p.Key], p.Value));
}

/// <summary>Stored in the existing audit journal; survives API/ingestion restarts without a schema change.</summary>
public sealed record HubCommandReceipt(string CommandId, string Action, string State, DateTimeOffset IssuedAt,
    DateTimeOffset Deadline, int? IfVersion, JsonObject Data, string? Code = null, string? Message = null,
    DateTimeOffset? AcknowledgedAt = null, string? DeviceTimeUtc = null, int? AppliedVersion = null, string? Actor = null, string? PayloadHash = null, string? Payload = null);

public sealed record HubCapturedMessage(string Topic, DateTimeOffset ReceivedAt, string Payload, int Bytes);
public sealed record HubMonitor(IReadOnlyList<HubCapturedMessage> Messages, int CommandsToday, double? SuccessRate);
