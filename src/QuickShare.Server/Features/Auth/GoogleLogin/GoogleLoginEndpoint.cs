using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace QuickShare.Server.Features.Auth.GoogleLogin;

public record GoogleLoginRequest(string? Redirect);

public sealed class GoogleLoginEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/auth/google/login");
        AllowAnonymous();
        Description(b => b.Produces(302));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var redirect = Query<string>("redirect") ?? "/swagger";

        var props = new AuthenticationProperties
        {
            RedirectUri = redirect,
            IsPersistent = true
        };

        await HttpContext.ChallengeAsync("Google", props);
    }
}
