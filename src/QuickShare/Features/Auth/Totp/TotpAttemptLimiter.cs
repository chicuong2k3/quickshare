using Microsoft.Extensions.Caching.Memory;

namespace QuickShare.Features.Auth.Totp;

internal static class TotpAttemptLimiter
{
    // Like the request limiter, this window is local to one app instance.
    private const int MaxAttempts = 10;
    private const int LockCount = 256;
    private const int SinglePermit = 1;
    private static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(1);
    private static readonly MemoryCache Windows = new(new MemoryCacheOptions());
    private static readonly object Sync = new();
    private static readonly SemaphoreSlim[] Locks = Enumerable.Range(0, LockCount)
        .Select(_ => new SemaphoreSlim(SinglePermit, SinglePermit))
        .ToArray();

    public static async Task<IDisposable> EnterAsync(Guid userId, CancellationToken ct)
    {
        var index = (int)((uint)userId.GetHashCode() % LockCount);
        var semaphore = Locks[index];
        await semaphore.WaitAsync(ct);
        return new Lease(semaphore);
    }

    public static bool TryAcquire(Guid userId, out int retryAfterSeconds) =>
        TryAcquire(userId, DateTime.UtcNow, out retryAfterSeconds);

    internal static bool TryAcquire(Guid userId, DateTime now, out int retryAfterSeconds)
    {
        lock (Sync)
        {
            if (!Windows.TryGetValue(userId, out AttemptWindow? window) || window is null)
            {
                window = new AttemptWindow();
            }

            while (window.Attempts.Count > 0 && now - window.Attempts.Peek() >= WindowDuration)
            {
                window.Attempts.Dequeue();
            }

            if (window.Attempts.Count >= MaxAttempts)
            {
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((window.Attempts.Peek() + WindowDuration - now).TotalSeconds));
                return false;
            }

            window.Attempts.Enqueue(now);
            Windows.Set(userId, window, WindowDuration);
            retryAfterSeconds = 0;
            return true;
        }
    }

    private sealed class AttemptWindow
    {
        public Queue<DateTime> Attempts { get; } = new();
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose()
        {
            semaphore.Release();
        }
    }
}
