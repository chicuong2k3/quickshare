namespace QuickShare.Server.Features.Users.Profile;

public sealed class UpdateUserProfileEndpoint : Endpoint<UpdateUserProfileRequest, UserProfileResponse>
{
    public override void Configure()
    {
        Put("/api/users/me/profile");
        Description(b => b.WithTags("Users"));
        Summary(s =>
        {
            s.Summary = "Update current user profile";
            s.Description = "Updates the authenticated user's display name and/or avatar URL.";
            s.Responses[200] = "The updated user profile.";
            s.Responses[400] = "Validation error (e.g. empty name).";
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

        // Execute direct database update (user entity is untracked from GetOrCreateUserAsync)
        if (req.AvatarUrl is null)
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
                    .SetProperty(u => u.AvatarUrl, req.AvatarUrl), ct);
        }

        // Fetch updated user for response
        var updatedUser = await db.Users
            .AsNoTracking()
            .FirstAsync(u => u.Id == userId, ct);

        await Send.OkAsync(new UserProfileResponse(
            updatedUser.Id,
            updatedUser.Name,
            updatedUser.Email,
            updatedUser.AvatarUrl,
            updatedUser.Plan,
            updatedUser.CreatedAt
        ), ct);
    }
}
