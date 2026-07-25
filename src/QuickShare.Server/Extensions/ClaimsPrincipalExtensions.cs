using System.Security.Claims;

namespace QuickShare.Server.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return id is not null ? Guid.Parse(id) : throw new InvalidOperationException("Not authenticated");
    }

    public static async Task<User> GetUserAsync(this ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var providerKey = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var provider = principal.FindFirstValue(AuthConstants.ProviderClaimType);

        if (string.IsNullOrEmpty(providerKey) || string.IsNullOrEmpty(provider))
            throw new InvalidOperationException("Missing auth claims");

        var login = await db.UserLogins
            .Include(ul => ul.User)
            .FirstOrDefaultAsync(ul => ul.Provider == provider && ul.ProviderKey == providerKey, ct);

        return login?.User ?? throw new InvalidOperationException(
            $"User not found for provider '{provider}' with key '{providerKey}'. " +
            "The user must be registered via POST /api/users/register before accessing this endpoint.");
    }
}
