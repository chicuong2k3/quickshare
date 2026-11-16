using QuickShare.Infrastructure.Email;

namespace QuickShare.Server.Tests.Infrastructure;

public sealed class SenderEmailRateLimiterTests
{
    [Fact]
    public void SameEmail_SixthAttemptInWindow_Denied()
    {
        var limiter = new SenderEmailRateLimiter();
        const string email = "a@example.com";
        const string ip = "10.0.0.1";

        for (var i = 0; i < 5; i++)
            Assert.True(limiter.TryAllow(email, ip));

        Assert.False(limiter.TryAllow(email, ip));
    }

    [Fact]
    public void SameIp_TwentyFirstAttemptInWindow_Denied()
    {
        var limiter = new SenderEmailRateLimiter();
        const string ip = "10.0.0.2";

        for (var i = 0; i < 20; i++)
            Assert.True(limiter.TryAllow($"u{i}@example.com", ip));

        Assert.False(limiter.TryAllow("u20@example.com", ip));
    }

    [Fact]
    public void DifferentEmail_SameIp_Allowed()
    {
        var limiter = new SenderEmailRateLimiter();

        Assert.True(limiter.TryAllow("a@example.com", "10.0.0.3"));
        Assert.True(limiter.TryAllow("b@example.com", "10.0.0.3"));
    }
}
