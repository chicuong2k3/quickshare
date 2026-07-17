using TeleCloud.Server.Data;
using TeleCloud.Server.Features.TempShares.Shared;

namespace TeleCloud.Server.Tests.TempShares;

public class TempShareExpiryTests
{
    [Fact]
    public void IsExpired_FutureExpiry_ReturnsFalse()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var share = NewShare(now.AddHours(1));

        Assert.False(TempSharePolicy.IsExpired(share, now));
    }

    [Fact]
    public void IsExpired_PastExpiry_ReturnsTrue()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var share = NewShare(now.AddHours(-1));

        Assert.True(TempSharePolicy.IsExpired(share, now));
    }

    [Fact]
    public void IsExpired_ExactlyAtExpiry_ReturnsTrue()
    {
        // Boundary: ExpiresAt <= now counts as expired (hard gate).
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var share = NewShare(now);

        Assert.True(TempSharePolicy.IsExpired(share, now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryParseExpiry_MissingValue_ReturnsDefault24h(string? expiry)
    {
        var parsed = TempSharePolicy.TryParseExpiry(expiry);

        Assert.Equal(TimeSpan.FromHours(24), parsed);
    }

    [Fact]
    public void TryParseExpiry_24h_ReturnsTwentyFourHours()
    {
        Assert.Equal(TimeSpan.FromHours(24), TempSharePolicy.TryParseExpiry("24h"));
    }

    [Fact]
    public void TryParseExpiry_7d_ReturnsSevenDays()
    {
        Assert.Equal(TimeSpan.FromDays(7), TempSharePolicy.TryParseExpiry("7d"));
    }

    [Theory]
    [InlineData("3h")]
    [InlineData("30d")]
    [InlineData("24H")]   // case-sensitive: only lowercase accepted
    [InlineData("foo")]
    public void TryParseExpiry_InvalidValue_ReturnsNull(string expiry)
    {
        Assert.Null(TempSharePolicy.TryParseExpiry(expiry));
    }

    [Fact]
    public void MaxUploadBytes_IsTwoGigabytes()
    {
        Assert.Equal(2L * 1024 * 1024 * 1024, TempSharePolicy.MaxUploadBytes);
    }

    private static TempShare NewShare(DateTime expiresAt) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            FileId = Guid.CreateVersion7(),
            ExpiresAt = expiresAt,
            CreatedAt = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc)
        };
}
