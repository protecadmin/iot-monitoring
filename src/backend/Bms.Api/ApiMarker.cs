namespace Bms.Api;

/// <summary>
/// Entry-point marker for <c>WebApplicationFactory</c> in the integration
/// tests. Needed because Bms.Ingestion also serves HTTP now (Kestrel, for
/// the Prometheus scrape endpoint), so both assemblies emit a public
/// top-level <c>Program</c> and <c>WebApplicationFactory&lt;Program&gt;</c>
/// no longer resolves to one of them. Referencing a type that exists in
/// exactly one assembly says which host to boot.
/// </summary>
public sealed class ApiMarker;
