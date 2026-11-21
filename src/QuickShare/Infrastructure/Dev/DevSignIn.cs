using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Auth.Passkeys;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Infrastructure.Dev;

/// <summary>
/// A sign-in shortcut for local screenshots and manual QA. It exists because the account,
/// checkout and admin surfaces cannot be rendered at all without a session, which left them
/// impossible to review.
///
/// Three gates, all of which must hold: the app is in Development, it is running the in-memory
/// database (so this can never touch a real one), and the caller presents the random token the
/// process printed at startup. The token is regenerated on every boot, so it cannot be bookmarked
/// or shared, and the route is mapped only when the same gate passes — production never has it.
/// It signs in through <see cref="AuthSignIn.CompleteAsync"/>, which starts a revocable session,
/// so the dev path obeys the same rule as every other way in.
/// </summary>
public static class DevSignIn
{
    public const string Route = "/__dev/signin";
    public const string KeyQuery = "key";
    public const string EmailQuery = "email";
    public const string ReturnQuery = "returnUrl";
    public const string DefaultEmail = "dev@localhost";
    public const string Provider = "dev";

    public static bool ShouldEnable(IConfiguration configuration, IHostEnvironment environment) =>
        environment.IsDevelopment()
        && configuration.GetValue<bool>("UseInMemoryDatabase");

    public static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    public static IEndpointRouteBuilder MapDevSignIn(this IEndpointRouteBuilder app, string token)
    {
        app.MapGet(Route, async (HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var key = context.Request.Query[KeyQuery].ToString();
            var email = context.Request.Query[EmailQuery].ToString();
            var returnUrl = context.Request.Query[ReturnQuery].ToString();

            if (!TokenMatches(key, token))
            {
                return Results.NotFound();
            }

            var address = string.IsNullOrWhiteSpace(email) ? DefaultEmail : email.Trim();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == address, ct);
            if (user is null)
            {
                user = new User { Name = "Dev account", Email = address, Plan = PlanTierLimits.Pro };
                db.Users.Add(user);
                await db.SaveChangesAsync(ct);
            }

            var hasLogin = await db.UserLogins.AnyAsync(l => l.UserId == user.Id && l.Provider == Provider, ct);
            if (!hasLogin)
            {
                db.UserLogins.Add(new UserLogin
                {
                    UserId = user.Id,
                    Provider = Provider,
                    ProviderKey = user.Id.ToString(),
                });
                await db.SaveChangesAsync(ct);
            }

            await AuthSignIn.CompleteAsync(context, db, user, Provider, ct);

            var destination = returnUrl is { Length: > 0 } ? returnUrl : RoutePaths.Account;
            return Results.Redirect(destination);
        }).AllowAnonymous();

        return app;
    }

    private static bool TokenMatches(string? supplied, string token)
    {
        if (string.IsNullOrEmpty(supplied))
        {
            return false;
        }

        var a = Encoding.UTF8.GetBytes(supplied);
        var b = Encoding.UTF8.GetBytes(token);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
