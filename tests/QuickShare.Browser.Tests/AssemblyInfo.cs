// Each BrowserFixture now gets its own InMemory store (InMemoryDatabaseName is set to a
// unique value), so test classes no longer share data or the per-IP P2P quota.
// Parallelism stays off because one Kestrel+Chromium at a time is far kinder to CI.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
