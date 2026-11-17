namespace Bms.Protocols.Transports;

/// <summary>MQTT topic-filter algebra (MQTT 5 spec §4.7).</summary>
public static class MqttTopicFilters
{
    /// <summary>
    /// True when every topic <paramref name="narrower"/> can match is also matched by
    /// <paramref name="broader"/> — e.g. <c>a/+/+</c> covers <c>a/+/b</c> and <c>a/x/b</c>;
    /// a filter covers itself.
    /// </summary>
    public static bool Covers(string broader, string narrower)
    {
        // A leading wildcard never matches topics that start with '$' (e.g. $SYS/...).
        if (narrower.StartsWith('$') && (broader.StartsWith('+') || broader.StartsWith('#')))
            return false;

        var b = broader.Split('/');
        var n = narrower.Split('/');
        for (var i = 0; i < b.Length; i++)
        {
            // '#' matches the parent level too ("a/#" matches "a"), so check it before running out of levels.
            if (b[i] == "#") return true;
            if (i >= n.Length || n[i] == "#") return false;
            if (b[i] == "+") continue;
            if (b[i] != n[i]) return false;
        }
        return b.Length == n.Length;
    }
}
