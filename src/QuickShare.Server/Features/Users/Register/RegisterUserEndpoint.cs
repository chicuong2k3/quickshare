using QuickShare.Server.Features.Users.Shared;

namespace QuickShare.Server.Features.Users.Register;

public sealed class RegisterUserEndpoint : Endpoint<RegisterUserRequest, ProfileResponse>
{
    public override void Configure()
    {
        Post("/api/users/register");
        AllowAnonymous();
        Description(b => b.WithTags("Users"));
        Summary(s =>
        {
            s.Summary = "Register current user from OAuth claims";
            s.Description = "Idempotent: creates user if not exists, returns existing user if already registered.";
        });
    }

    public override async Task HandleAsync(RegisterUserRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();

        var existing = await db.UserLogins
            .Include(l => l.User)
            .FirstOrDefaultAsync(l => l.Provider == req.Provider && l.ProviderKey == req.ProviderKey, ct);

        if (existing?.User is not null)
        {
            var u = existing.User;
            if (u.Name != req.Name || u.Email != req.Email)
            {
                u.Name = req.Name;
                u.Email = req.Email;
                await db.SaveChangesAsync(ct);
            }
            await Send.OkAsync(u.ToProfileResponse(), ct);
            return;
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Name = req.Name,
            Email = req.Email,
            AvatarUrl = req.AvatarUrl ?? string.Empty,
            Plan = PlanTierLimits.Free
        };
        db.Users.Add(user);

        db.UserLogins.Add(new UserLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Provider = req.Provider,
            ProviderKey = req.ProviderKey
        });

        var now = DateTime.UtcNow;
        db.UserUsages.Add(new UserUsage
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            PeriodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            TotalBytesUploaded = 0,
            TransferCount = 0
        });

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(user.ToProfileResponse(), ct);
    }
}

public sealed record RegisterUserRequest(string Provider, string ProviderKey, string Name, string Email, string? AvatarUrl);
