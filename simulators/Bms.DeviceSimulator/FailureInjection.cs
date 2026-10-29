using System.Text;

namespace Bms.DeviceSimulator;

/// <summary>
/// Deliberately breaks messages on a configurable rate — see core.txt
/// section 39/53: the platform must demonstrate malformed JSON, duplicate
/// messages, invalid values, etc., not just the happy path.
/// </summary>
public static class FailureInjection
{
    public static byte[] MaybeCorrupt(byte[] payload, double malformedRate)
    {
        if (malformedRate <= 0 || Random.Shared.NextDouble() >= malformedRate)
            return payload;

        var text = Encoding.UTF8.GetString(payload);
        return Encoding.UTF8.GetBytes(text.Length > 5 ? text[..^5] : "{bad"); // truncate to produce invalid JSON
    }

    public static bool ShouldDuplicate(double duplicateRate) =>
        duplicateRate > 0 && Random.Shared.NextDouble() < duplicateRate;
}
