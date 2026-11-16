using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using QuickShare.Features.Workspaces.Shared;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Auth.MagicLink;

/// <summary>
/// Passwordless sign-in by emailed link (issue #132).
///
/// Two properties carry the whole feature, and both are about what it refuses to say:
///
/// - **Requesting a link always looks the same.** Same response, same timing shape, whether or not
///   the address has an account. Otherwise this endpoint is a free account-existence oracle for
///   anybody with a list of email addresses.
/// - **A link is single-use and short-lived.** It sits in an inbox, and an inbox is somewhere other
///   people occasionally have access to.
/// </summary>
public sealed class MagicLinkService(
    AppDbContext db,
    EmailSender email,
    IOptions<EmailOptions> emailOptions,
    ILogger<MagicLinkService> logger)
{
    /// <summary>Links per address per window. A mailbox is not a place to send unlimited mail.</summary>
    private const int MaxPerWindow = 5;
    private static readonly TimeSpan RateWindow = TimeSpan.FromHours(1);

    /// <summary>
    /// Sends a link if the address has an account, and says nothing either way.
    ///
    /// The caller gets the same answer regardless — that is not vagueness for its own sake, it is
    /// the difference between "somebody can check whether you have an account here" and not.
    /// </summary>
    public async Task RequestAsync(string? rawEmail, CancellationToken ct)
    {
        var address = (rawEmail ?? string.Empty).Trim().ToLowerInvariant();
        if (address.Length == 0 || address.Length > LoginToken.EmailMaxLength || !address.Contains('@', StringComparison.Ordinal))
            return;

        var since = DateTime.UtcNow - RateWindow;
        var recent = await db.LoginTokens.CountAsync(t => t.Email == address && t.CreatedAt > since, ct);
        if (recent >= MaxPerWindow)
        {
            logger.LogWarning("Magic link rate limit reached for an address");
            return;
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == address, ct);
        if (user is null)
        {
            // Deliberately silent. Creating an account here would turn a sign-in form into a
            // registration form that anybody can point at anybody else's address.
            return;
        }

        var raw = GenerateToken();

        db.LoginTokens.Add(new LoginToken
        {
            Id = Guid.CreateVersion7(),
            Email = address,
            TokenHash = WorkspaceService.HashToken(raw),
            ExpiresAt = DateTime.UtcNow.Add(LoginToken.Lifetime),
        });
        await db.SaveChangesAsync(ct);

        var link = $"{emailOptions.Value.BaseUrl.TrimEnd('/')}{RoutePaths.MagicLink}?token={Uri.EscapeDataString(raw)}";
        var minutes = (int)LoginToken.Lifetime.TotalMinutes;

        // The send must never change what the caller sees.
        //
        // This is not defensive habit — a test caught it: with email unconfigured, a known
        // address threw and an unknown one returned quietly, which is precisely the
        // account-existence oracle the rest of this method exists to avoid. A failure to send is
        // an operational problem, and operational problems belong in the log, not in a response
        // that a stranger can diff against another one.
        try
        {
            await email.SendAsync(
                address,
                user.Name,
                "Your sign-in link",
                $"<p>Use this link to sign in. It works once and expires in {minutes} minutes.</p>"
                + $"<p><a href=\"{link}\">Sign in</a></p>"
                + "<p>If you did not ask for this, you can ignore it — nothing has changed on your account.</p>",
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to send a magic-link email");
        }
    }

    /// <summary>
    /// Spends a token and returns the account it proves, or null.
    ///
    /// Marked used before anything else happens, so two clicks — the human one and the one from a
    /// link-scanning mail gateway — cannot both succeed.
    /// </summary>
    public async Task<User?> ConsumeAsync(string? rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return null;

        var hash = WorkspaceService.HashToken(rawToken.Trim());
        var token = await db.LoginTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is null || !token.IsUsable(DateTime.UtcNow))
            return null;

        token.UsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return await db.Users.FirstOrDefaultAsync(u => u.Email == token.Email, ct);
    }

    private static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
