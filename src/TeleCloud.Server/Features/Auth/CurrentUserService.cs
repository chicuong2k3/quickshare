using System.Security.Claims;

namespace TeleCloud.Server.Features.Auth;

public class CurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TeleCloudDbContext _db;
    private User? _user;
    private bool _resolved;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor, TeleCloudDbContext db)
    {
        _httpContextAccessor = httpContextAccessor;
        _db = db;
    }

    public async Task<User?> GetUserAsync(CancellationToken ct)
    {
        if (_resolved) return _user;
        _resolved = true;

        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        var providerKey = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var provider = principal.FindFirstValue("provider");
        var name = principal.FindFirstValue(ClaimTypes.Name) ?? "Unknown";
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? "";
        var avatar = principal.FindFirstValue("avatar_url") ?? "";

        if (string.IsNullOrEmpty(providerKey) || string.IsNullOrEmpty(provider))
            return null;

        _user = await GetOrCreateUserAsync(provider, providerKey, name, email, avatar, ct);
        return _user;
    }

    public async Task<Guid> GetUserIdAsync(CancellationToken ct)
    {
        var user = await GetUserAsync(ct);
        return user?.Id ?? throw new InvalidOperationException("Auth middleware should have rejected this request");
    }

    private async Task<User> GetOrCreateUserAsync(
        string provider, string providerKey, string name, string email, string avatar, CancellationToken ct)
    {
        var existing = await _db.UserLogins
            .Include(ul => ul.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(ul => ul.Provider == provider && ul.ProviderKey == providerKey, ct);

        if (existing?.User is not null)
            return existing.User;

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            Email = email,
            AvatarUrl = avatar,
            CreatedAt = DateTime.UtcNow
        };

        var login = new UserLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Provider = provider,
            ProviderKey = providerKey
        };

        _db.Users.Add(user);
        _db.UserLogins.Add(login);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            var winner = await _db.UserLogins
                .Include(ul => ul.User)
                .AsNoTracking()
                .FirstAsync(ul => ul.Provider == provider && ul.ProviderKey == providerKey, ct);
            return winner.User!;
        }

        return user;
    }
}
