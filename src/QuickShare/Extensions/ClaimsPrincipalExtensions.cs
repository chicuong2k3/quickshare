using System.Security.Claims;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return id is not null ? Guid.Parse(id) : throw new InvalidOperationException("Not authenticated");
    }

    /// <summary>Like <see cref="GetUserAsync"/> but returns null for anonymous requests.</summary>
    public static async Task<User?> GetUserOrDefaultAsync(this ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var providerKey = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var provider = principal.FindFirstValue(AuthConstants.ProviderClaimType);

        if (string.IsNullOrEmpty(providerKey) || string.IsNullOrEmpty(provider))
            return null;

        var login = await db.UserLogins
            .Include(ul => ul.User)
            .FirstOrDefaultAsync(ul => ul.Provider == provider && ul.ProviderKey == providerKey, ct);

        return login?.User;
    }

    public static async Task<User> GetUserAsync(this ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var providerKey = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var provider = principal.FindFirstValue(AuthConstants.ProviderClaimType);

        if (string.IsNullOrEmpty(providerKey) || string.IsNullOrEmpty(provider))
            throw new InvalidOperationException("Missing auth claims");

        var cache = StaticCache.Instance;
        return await cache.GetOrSetAsync(
            $"user:login:{provider}:{providerKey}",
            async ct2 =>
            {
                var login = await db.UserLogins
                    .Include(ul => ul.User)
                    .FirstOrDefaultAsync(ul => ul.Provider == provider && ul.ProviderKey == providerKey, ct2);

                return login?.User ?? throw new InvalidOperationException(
                    $"User not found for provider '{provider}' with key '{providerKey}'. " +
                    "The user must be registered via POST /api/users/register before accessing this endpoint.");
            },
            duration: TimeSpan.FromMinutes(5),
            token: ct
        );
    }
}
