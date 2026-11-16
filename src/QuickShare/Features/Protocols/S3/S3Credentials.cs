using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Protocols.S3;

/// <summary>A verified gateway caller: which workspace, acting as whom, allowed to do what.</summary>
public sealed record S3Session(WorkspaceS3Credential Credential, User User, Workspace Workspace)
{
    public Guid WorkspaceId => Workspace.Id;

    public bool Grants(string scope) => ApiScopes.Grants(Credential.Scopes, scope);
}

/// <summary>
/// Issues and verifies S3 access keys (issue #265).
///
/// The secret is sealed with the app's DataProtection keyring — the same one the sender-email
/// and unsubscribe tokens use, persisted to a volume in the container, so this is an existing
/// operational surface rather than a new one. If that keyring is ever lost, these credentials
/// stop verifying and have to be reissued: that is the honest failure mode, and it is written
/// down rather than worked around with a second secret store.
/// </summary>
public sealed class S3Credentials(AppDbContext db, IDataProtectionProvider protection, WorkspaceService workspaces)
{
    private const string Purpose = "QuickShare.S3Credential.v1";
    private const string AccessKeyAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private readonly IDataProtector _protector = protection.CreateProtector(Purpose);

    /// <summary>Creates a pair. The secret is returned here and never again.</summary>
    public async Task<(WorkspaceS3Credential Credential, string Secret)> IssueAsync(
        Guid workspaceId, Guid userId, string label, string scopes, CancellationToken ct)
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(WorkspaceS3Credential.SecretBytes));

        var credential = new WorkspaceS3Credential
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            UserId = userId,
            Label = label,
            AccessKeyId = NewAccessKeyId(),
            SecretProtected = _protector.Protect(secret),
            Scopes = ApiScopes.Normalize(scopes),
            CreatedAt = DateTime.UtcNow,
        };

        db.WorkspaceS3Credentials.Add(credential);
        await db.SaveChangesAsync(ct);

        return (credential, secret);
    }

    /// <summary>
    /// Verifies a signed request. Null for every failure — a caller that could tell "no such key"
    /// from "bad signature" could enumerate which access keys exist.
    /// </summary>
    public async Task<S3Session?> AuthenticateAsync(HttpContext context, CancellationToken ct)
    {
        var signed = SigV4.Parse(context.Request);
        if (signed is null)
            return null;

        var credential = await db.WorkspaceS3Credentials
            .Include(c => c.User)
            .Include(c => c.Workspace)
            .FirstOrDefaultAsync(c => c.AccessKeyId == signed.AccessKeyId && c.RevokedAt == null, ct);

        if (credential is null)
            return null;

        string secret;
        try
        {
            secret = _protector.Unprotect(credential.SecretProtected);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // The keyring no longer opens this secret. Nothing can be done at request time and
            // guessing is not an option: the owner reissues.
            return null;
        }

        if (!SigV4.Verify(context.Request, signed, secret, DateTime.UtcNow))
            return null;

        // The seat is checked on every request, not just at issue: revoking somebody's seat has
        // to close their gateway access too, or the key outlives the membership.
        var seat = await workspaces.GetSeatAsync(credential.UserId, ct);
        if (seat is null || !seat.IsActive || seat.WorkspaceId != credential.WorkspaceId)
            return null;

        credential.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new S3Session(credential, credential.User, credential.Workspace);
    }

    /// <summary>The signing key for this request, needed to verify a chunked body.</summary>
    public byte[] SigningKeyFor(WorkspaceS3Credential credential, SigV4Request request) =>
        SigV4.SigningKey(_protector.Unprotect(credential.SecretProtected), request.Date, request.Region);

    private static string NewAccessKeyId()
    {
        var body = RandomNumberGenerator.GetItems<char>(
            AccessKeyAlphabet, WorkspaceS3Credential.AccessKeyIdLength - WorkspaceS3Credential.AccessKeyPrefix.Length);

        return WorkspaceS3Credential.AccessKeyPrefix + new string(body);
    }
}
