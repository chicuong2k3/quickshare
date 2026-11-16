using System.Collections.Concurrent;
using Hangfire;

namespace QuickShare.Features.Developer.ApiKeys;

/// <summary>
/// Counts API calls per key, per day (issue #273).
///
/// **In memory, flushed on a tick.** A key that authenticates a request has already cost a
/// lookup and a `LastUsedAt` write; adding a second write per call to keep a counter would make
/// the analytics more expensive than the thing being measured. Counts are buffered and written
/// once a minute.
///
/// ponytail: the buffer is process-local, so a hard kill loses up to a minute of counts and
/// several instances each flush their own share. Both are fine for a usage graph and neither is
/// fine for billing — if this ever meters money, it needs a durable path, not a bigger buffer.
/// </summary>
public sealed class ApiKeyUsageRecorder
{
    /// <summary>
    /// Keyed by key id and UTC date: the only two dimensions the dashboard shows.
    ///
    /// An instance field on a singleton, not a static one. Same lifetime in production, and it
    /// keeps the buffer inside the application that owns it — a static would be shared by every
    /// app in the process, which is exactly what a test host runs.
    /// </summary>
    private readonly ConcurrentDictionary<(Guid KeyId, DateOnly Day), int> _pending = new();

    public void Record(Guid keyId, DateTime utcNow) =>
        _pending.AddOrUpdate((keyId, DateOnly.FromDateTime(utcNow)), 1, (_, count) => count + 1);

    /// <summary>Takes everything buffered so far and leaves the buffer empty.</summary>
    public IReadOnlyList<(Guid KeyId, DateOnly Day, int Count)> Drain()
    {
        var drained = new List<(Guid, DateOnly, int)>();

        foreach (var key in _pending.Keys.ToList())
        {
            if (_pending.TryRemove(key, out var count) && count > 0)
                drained.Add((key.KeyId, key.Day, count));
        }

        return drained;
    }
}

/// <summary>
/// Writes the buffered counts (issue #273).
///
/// Adds to whatever is already stored rather than replacing it, because two instances flushing
/// the same day must sum rather than race to be last.
/// </summary>
public sealed class ApiKeyUsageFlushJob(
    IServiceScopeFactory scopeFactory, ApiKeyUsageRecorder recorder, ILogger<ApiKeyUsageFlushJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task FlushAsync()
    {
        var drained = recorder.Drain();
        if (drained.Count == 0)
            return;

        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var (keyId, day, count) in drained)
        {
            try
            {
                var updated = await db.ApiKeyUsages
                    .Where(u => u.ApiKeyId == keyId && u.Day == day)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.Requests, u => u.Requests + count), ct);

                if (updated == 0)
                {
                    db.ApiKeyUsages.Add(new ApiKeyUsage
                    {
                        Id = Guid.CreateVersion7(),
                        ApiKeyId = keyId,
                        Day = day,
                        Requests = count,
                    });
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex)
            {
                // A lost count is a gap in a graph, not an incident. Never let it take the tick
                // down and lose the rest of the batch with it.
                logger.LogWarning(ex, "Could not flush API usage for key {KeyId}", keyId);
            }
        }
    }
}
