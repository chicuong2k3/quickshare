using System.Security.Claims;

namespace QuickShare.Server.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return id is not null ? Guid.Parse(id) : throw new InvalidOperationException("Not authenticated");
    }

    public static async Task<User> GetOrCreateUserAsync(this ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var providerKey = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var provider = principal.FindFirstValue("provider");
        var name = principal.FindFirstValue(ClaimTypes.Name) ?? "Unknown";
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        var avatar = principal.FindFirstValue("avatar_url") ?? string.Empty;

        if (string.IsNullOrEmpty(providerKey) || string.IsNullOrEmpty(provider))
            throw new InvalidOperationException("Missing auth claims");

        var existing = await db.UserLogins
            .Include(ul => ul.User)
            .FirstOrDefaultAsync(ul => ul.Provider == provider && ul.ProviderKey == providerKey, ct);

        if (existing?.User is not null)
            return existing.User;

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            Email = email,
            AvatarUrl = avatar
        };

        var login = new UserLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Provider = provider,
            ProviderKey = providerKey
        };

        db.Users.Add(user);
        db.UserLogins.Add(login);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await db.UserLogins
                .Include(ul => ul.User)
                .AsNoTracking()
                .FirstAsync(ul => ul.Provider == provider && ul.ProviderKey == providerKey, ct);
            return winner.User!;
        }

        return user;
    }
}
