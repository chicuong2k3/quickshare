using QuickShare.Features.Users.Shared;

namespace QuickShare.Features.Users.Register;

public sealed class RegisterUserEndpoint : Endpoint<RegisterUserRequest, ProfileResponse>
{
    public override void Configure()
    {
        Post("/api/users/register");
        AllowAnonymous();
        Validator<RegisterUserValidator>();
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

        var profile = await UserRegistrationService.RegisterAsync(
            req.Provider, req.ProviderKey, req.Name, req.Email, req.AvatarUrl, db, ct);

        await Send.OkAsync(profile, ct);
    }
}

public sealed record RegisterUserRequest(string Provider, string ProviderKey, string Name, string Email, string? AvatarUrl);
