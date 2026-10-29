using Xunit;

// All integration tests share one Postgres database via BmsApiFactory;
// running them in parallel isn't safe (verified: caused deadlocks during
// seeding). This attribute is honored reliably by the VSTest adapter,
// unlike xunit.runner.json's parallelizeTestCollections in some setups.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
