using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace QuickShare.Infrastructure.FeatureFlags;

/// <summary>
/// Config-based admin gate: a principal is admin when its email claim is listed
/// in <see cref="AdminOptions.Emails"/>. Deliberately claim-based (no DB hit) so
/// the gate works on every request path, including the Razor page shell render.
/// </summary>
public static class AdminGate
{
    public static bool IsAdmin(this ClaimsPrincipal user, AdminOptions options)
    {
        var email = user.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(email)) return false;
        return options.Emails.Contains(email, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Convenience for DI-free call sites (endpoints resolve options via <see cref="IOptions{T}"/>).</summary>
    public static bool IsAdmin(this ClaimsPrincipal user, IOptions<AdminOptions> options) =>
        user.IsAdmin(options.Value);
}