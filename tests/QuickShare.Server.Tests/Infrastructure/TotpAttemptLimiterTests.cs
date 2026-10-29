using QuickShare.Features.Auth.Totp;

namespace QuickShare.Server.Tests.Infrastructure;

public sealed class TotpAttemptLimiterTests
{
    private const int AttemptLimit = 10;
    private const int ConcurrentAttempts = 50;

    [Fact]
    public void Limit_UsesRollingWindowAcrossMinuteBoundaries()
    {
        var userId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        for (var i = 0; i < AttemptLimit; i++)
        {
            Assert.True(TotpAttemptLimiter.TryAcquire(userId, now, out _));
        }

        Assert.False(TotpAttemptLimiter.TryAcquire(userId, now.AddSeconds(59), out var retry));
        Assert.Equal(1, retry);
        Assert.True(TotpAttemptLimiter.TryAcquire(userId, now.AddSeconds(61), out _));
    }

    [Fact]
    public async Task ConcurrentRequests_CannotExceedLimit()
    {
        var userId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        var attempts = Enumerable.Range(0, ConcurrentAttempts)
            .Select(index => Task.Run(() => TotpAttemptLimiter.TryAcquire(userId, now, out _)));

        var results = await Task.WhenAll(attempts);

        Assert.Equal(AttemptLimit, results.Count(allowed => allowed));
    }

    [Fact]
    public async Task VerificationLock_WaitsForPreviousOperation()
    {
        var userId = Guid.CreateVersion7();
        var first = await TotpAttemptLimiter.EnterAsync(userId, CancellationToken.None);
        var secondTask = TotpAttemptLimiter.EnterAsync(userId, CancellationToken.None);
        try
        {
            Assert.False(secondTask.IsCompleted);
        }
        finally
        {
            first.Dispose();
        }

        using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(second);
    }
}
