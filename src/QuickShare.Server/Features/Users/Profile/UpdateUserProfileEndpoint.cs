using QuickShare.Server.Features.Users.Shared;

namespace QuickShare.Server.Features.Users.Profile;

public sealed class UpdateUserProfileEndpoint : Endpoint<UpdateUserProfileRequest, ProfileResponse>
{
    public override void Configure()
    {
        Put("/api/users/me/profile");
        Description(b => b.WithTags("Users"));
        Summary(s =>
        {
            s.Summary = "Update current user profile";
            s.Description = "Updates the authenticated user's display name and/or avatar.";
            s.Responses[200] = "The updated user profile.";
            s.Responses[400] = "Validation error.";
            s.Responses[401] = "User is not authenticated.";
        });
    }

    public override async Task HandleAsync(UpdateUserProfileRequest req, CancellationToken ct)
    {
        var principal = HttpContext.User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            HttpContext.Response.StatusCode = 401;
            return;
        }

        var db = Resolve<AppDbContext>();
        var user = await principal.GetOrCreateUserAsync(db, ct);
        var userId = user.Id;

        var avatarUrl = string.IsNullOrEmpty(req.AvatarBase64)
            ? null
            : $"data:{DeriveMimeType(req.AvatarBase64)};base64,{StripDataUriPrefix(req.AvatarBase64)}";

        if (avatarUrl is null)
        {
            await db.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(u => u.Name, req.Name), ct);
        }
        else
        {
            await db.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(u => u.Name, req.Name)
                    .SetProperty(u => u.AvatarUrl, avatarUrl), ct);
        }

        var updatedUser = await db.Users
            .AsNoTracking()
            .FirstAsync(u => u.Id == userId, ct);

        await Send.OkAsync(updatedUser.ToProfileResponse(), ct);
    }

    private static string DeriveMimeType(string base64)
    {
        if (base64.StartsWith("data:"))
        {
            var end = base64.IndexOf(';', 5);
            if (end > 5) return base64[5..end];
        }
        return "image/png";
    }

    private static string StripDataUriPrefix(string base64)
    {
        var comma = base64.IndexOf(',');
        return comma >= 0 ? base64[(comma + 1)..] : base64;
    }
}
