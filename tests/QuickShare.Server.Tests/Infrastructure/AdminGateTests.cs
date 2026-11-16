using System.Security.Claims;
using QuickShare.Infrastructure.FeatureFlags;

namespace QuickShare.Tests.Infrastructure;

/// <summary>
/// Locks the email-claim admin gate that also backs the Hangfire dashboard
/// authorization filter (<c>AdminDashboardAuthorizationFilter</c>).
/// </summary>
public sealed class AdminGateTests
{
    private const string AdminEmail = "nguyenchicuongk21@gmail.com";

    private static ClaimsPrincipal PrincipalWithEmail(string? email)
    {
        var identity = new ClaimsIdentity();
        if (email is not null)
            identity.AddClaim(new Claim(ClaimTypes.Email, email));
        return new ClaimsPrincipal(identity);
    }

    private static AdminOptions Options(params string[] emails) => new() { Emails = emails };

    [Fact]
    public void IsAdmin_ReturnsTrue_WhenEmailIsListed()
    {
        var user = PrincipalWithEmail(AdminEmail);

        Assert.True(user.IsAdmin(Options(AdminEmail)));
    }

    [Fact]
    public void IsAdmin_IgnoresEmailCase()
    {
        var user = PrincipalWithEmail(AdminEmail.ToUpperInvariant());

        Assert.True(user.IsAdmin(Options(AdminEmail)));
    }

    [Fact]
    public void IsAdmin_ReturnsFalse_WhenEmailNotListed()
    {
        var user = PrincipalWithEmail("someone@else.com");

        Assert.False(user.IsAdmin(Options(AdminEmail)));
    }

    [Fact]
    public void IsAdmin_ReturnsFalse_WhenNoEmailClaim()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.False(user.IsAdmin(Options(AdminEmail)));
    }

    [Fact]
    public void IsAdmin_ReturnsFalse_WhenEmailBlank()
    {
        var user = PrincipalWithEmail("   ");

        Assert.False(user.IsAdmin(Options(AdminEmail)));
    }

    [Fact]
    public void IsAdmin_ReturnsFalse_WhenNoAdminsConfigured()
    {
        var user = PrincipalWithEmail(AdminEmail);

        Assert.False(user.IsAdmin(Options()));
    }
}
