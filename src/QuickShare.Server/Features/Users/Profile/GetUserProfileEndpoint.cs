using QuickShare.Server.Features.Users.Shared;

namespace QuickShare.Server.Features.Users.Profile;

public sealed class GetUserProfileEndpoint : Endpoint<EmptyRequest, ProfileResponse>
{
    public override void Configure()
    {
        Get("/api/users/me/profile");
        Description(b => b.WithTags("Users"));
        Summary(s =>
        {
            s.Summary = "Get current user profile";
            s.Description = "Returns the authenticated user's profile (name, email, avatar, plan, timestamps) from the database.";
            s.Responses[200] = "The user profile.";
            s.Responses[401] = "User is not authenticated.";
        });
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var principal = HttpContext.User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            HttpContext.Response.StatusCode = 401;
            return;
        }

        var db = Resolve<AppDbContext>();
        var user = await principal.GetOrCreateUserAsync(db, ct);

        await Send.OkAsync(user.ToProfileResponse(), ct);
    }
}
