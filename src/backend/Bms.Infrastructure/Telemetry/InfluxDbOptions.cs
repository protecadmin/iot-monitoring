namespace Bms.Infrastructure.Telemetry;

public sealed class InfluxDbOptions
{
    public const string SectionName = "InfluxDb";

    public string Url { get; set; } = "http://localhost:8086";
    public string Org { get; set; } = "bms";
    public string Bucket { get; set; } = "telemetry";
    public string Token { get; set; } = string.Empty;

    /// <summary>Per-request timeout, so a hung InfluxDB fails fast into the backfill instead of stalling ingestion.</summary>
    public int TimeoutSeconds { get; set; } = 5;
}
