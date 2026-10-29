using Xunit;

namespace Bms.IntegrationTests;

/// <summary>
/// One BmsApiFactory (one migrate + one seed run) shared across every test
/// class in this assembly. Without this, xUnit's default parallelism
/// spins up multiple WebApplicationFactory instances that race to migrate
/// and seed the same Postgres database concurrently — real deadlocks and
/// duplicate-key errors were observed here, not merely theoretical.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<BmsApiFactory>
{
    public const string Name = "Integration";
}
