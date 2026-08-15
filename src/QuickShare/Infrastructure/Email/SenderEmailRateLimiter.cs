using System.Collections.Concurrent;

namespace QuickShare.Infrastructure.Email;

/// <summary>
/// In-memory rate limiter for anonymous sender-email capture (issue #76).
/// One window per email (5 deliveries/hour) and one per IP (20 captures/hour).
/// Over the limit the link email is skipped — the upload itself always succeeds.
/// </summary>
public sealed class SenderEmailRateLimiter
{
    private const int MaxPerEmail = 5;
    private const int MaxPerIp = 20;
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, RateWindow> _windows = new();

    public bool TryAllow(string email, string ip)
    {
        var now = DateTime.UtcNow;
        return Allow($"email:{email}", MaxPerEmail, now) && Allow($"ip:{ip}", MaxPerIp, now);
    }

    private bool Allow(string key, int limit, DateTime now)
    {
        var window = _windows.GetOrAdd(key, _ => new RateWindow(now));

        lock (window)
        {
            if (now - window.Start > Window)
            {
                window.Start = now;
                window.Count = 0;
            }

            window.Count++;
            return window.Count <= limit;
        }
    }

    private sealed class RateWindow
    {
        public DateTime Start;
        public int Count;

        public RateWindow(DateTime start)
        {
            Start = start;
            Count = 0;
        }
    }
}
