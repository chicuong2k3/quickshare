using Xunit;

// Multiple WebApplicationFactory hosts booting the same Program type in one
// process race FastEndpoints static route/config caches and Wolverine statics:
// E2E responses intermittently pick up another host's headers/status (204
// where 423 was sent, missing X-Robots-Tag, etc.). Run tests sequentially —
// same precedent as QuickShare.Browser.Tests.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
