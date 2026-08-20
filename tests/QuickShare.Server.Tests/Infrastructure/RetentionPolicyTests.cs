using QuickShare.Infrastructure;

namespace QuickShare.Server.Tests.Infrastructure;

/// <summary>
/// Organizational retention policy (issue #161). The invariant under test: the policy is
/// a ceiling, never a floor — a sender's shorter choice always survives, and an unpolicied
/// account is never clamped.
/// </summary>
public sealed class RetentionPolicyTests
{
    private const int Window = 14;

    [Fact]
    public void NoPolicy_LeavesExpiryUntouched()
    {
        var expiry = DateTime.UtcNow.AddDays(90);

        Assert.Equal(expiry, RetentionPolicy.Clamp(expiry, null));
    }

    [Fact]
    public void ExpiryBeyondWindow_IsClampedToTheCeiling()
    {
        var expiry = DateTime.UtcNow.AddDays(90);

        var clamped = RetentionPolicy.Clamp(expiry, Window);

        Assert.True(clamped < expiry);
        Assert.True(clamped <= DateTime.UtcNow.AddDays(Window).AddSeconds(1));
    }

    [Fact]
    public void ExpiryInsideWindow_SurvivesClamping()
    {
        var expiry = DateTime.UtcNow.AddDays(3);

        Assert.Equal(expiry, RetentionPolicy.Clamp(expiry, Window));
    }

    [Fact]
    public void NullExpiryUnderPolicy_BecomesTheCeiling()
    {
        var clamped = RetentionPolicy.Clamp((DateTime?)null, Window);

        Assert.NotNull(clamped);
        Assert.True(clamped > DateTime.UtcNow);
        Assert.True(clamped <= DateTime.UtcNow.AddDays(Window).AddSeconds(1));
    }

    [Fact]
    public void NullExpiryWithoutPolicy_StaysNull()
    {
        Assert.Null(RetentionPolicy.Clamp((DateTime?)null, null));
    }

    [Fact]
    public void Ceiling_IsNullWithoutPolicy()
    {
        Assert.Null(RetentionPolicy.Ceiling(null));
    }

    [Fact]
    public void Ceiling_TracksTheWindow()
    {
        var ceiling = RetentionPolicy.Ceiling(Window);

        Assert.NotNull(ceiling);
        Assert.InRange(
            ceiling.Value,
            DateTime.UtcNow.AddDays(Window).AddMinutes(-1),
            DateTime.UtcNow.AddDays(Window).AddMinutes(1));
    }

    [Fact]
    public void Exceeds_IsFalseWithoutPolicy()
    {
        Assert.False(RetentionPolicy.Exceeds(DateTime.UtcNow.AddYears(5), null));
        Assert.False(RetentionPolicy.Exceeds(null, null));
    }

    [Fact]
    public void Exceeds_FlagsRowsBeyondTheWindow()
    {
        Assert.True(RetentionPolicy.Exceeds(DateTime.UtcNow.AddDays(Window + 1), Window));
        Assert.False(RetentionPolicy.Exceeds(DateTime.UtcNow.AddDays(Window - 1), Window));
    }

    [Fact]
    public void Exceeds_TreatsNeverExpiresAsOutOfPolicy()
    {
        Assert.True(RetentionPolicy.Exceeds(null, Window));
    }

    [Fact]
    public void Exceeds_FlagsAlreadyExpiredRowsAsCompliant()
    {
        Assert.False(RetentionPolicy.Exceeds(DateTime.UtcNow.AddDays(-1), Window));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(RetentionPolicy.MinDays)]
    [InlineData(14)]
    [InlineData(RetentionPolicy.MaxDays)]
    public void ValidWindows_AreAccepted(int? days)
    {
        Assert.True(RetentionPolicy.IsValid(days));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(RetentionPolicy.MaxDays + 1)]
    public void OutOfRangeWindows_AreRejected(int days)
    {
        Assert.False(RetentionPolicy.IsValid(days));
    }

    [Fact]
    public void EveryPreset_IsAValidWindow()
    {
        Assert.All(RetentionPolicy.Presets, days => Assert.True(RetentionPolicy.IsValid(days)));
    }
}
