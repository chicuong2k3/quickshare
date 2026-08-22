using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Auth.Passkeys;

/// <summary>
/// WebAuthn registration and assertion (issue #68).
///
/// The cryptography is Fido2NetLib's. What lives here is the part a library cannot do for us:
/// deciding which credentials belong to whom, keeping the challenge alive across the two halves
/// of a ceremony, and refusing a replay.
///
/// **Passkeys are an additional way in, not a replacement.** Every account here is created through
/// an identity provider, so losing every passkey is not a lockout — the original provider still
/// works. That shapes what this does and does not need to guard against.
/// </summary>
public sealed class PasskeyService(AppDbContext db, IFido2 fido2, IOptions<EmailOptions> emailOptions)
{
    /// <summary>Begins registration for a signed-in account, and stores the challenge.</summary>
    public async Task<(string Handle, CredentialCreateOptions Options)> BeginRegistrationAsync(
        User user, CancellationToken ct)
    {
        var existing = await db.UserPasskeys
            .AsNoTracking()
            .Where(p => p.UserId == user.Id)
            .Select(p => p.CredentialId)
            .ToListAsync(ct);

        var options = fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User
            {
                // The account id, not the email: a user handle is meant to be an opaque,
                // non-reassignable identifier, and email addresses are neither.
                Id = user.Id.ToByteArray(),
                Name = user.Email,
                DisplayName = string.IsNullOrWhiteSpace(user.Name) ? user.Email : user.Name,
            },
            // Excluding what is already registered stops an authenticator silently creating a
            // second credential for the same account and leaving a stale one behind.
            ExcludeCredentials = [.. existing.Select(id => new PublicKeyCredentialDescriptor(Base64Url.DecodeFromChars(id)))],
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Preferred,
                UserVerification = UserVerificationRequirement.Preferred,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });

        var handle = await StoreChallengeAsync(options.ToJson(), user.Id, ct);
        return (handle, options);
    }

    /// <summary>Completes registration, verifying the attestation against the issued challenge.</summary>
    public async Task<UserPasskey?> CompleteRegistrationAsync(
        User user, string handle, AuthenticatorAttestationRawResponse response, string? name, CancellationToken ct)
    {
        var challenge = await ConsumeChallengeAsync(handle, ct);
        if (challenge is null || challenge.UserId != user.Id)
            return null;

        var options = CredentialCreateOptions.FromJson(challenge.OptionsJson);

        var credential = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
        {
            AttestationResponse = response,
            OriginalOptions = options,
            IsCredentialIdUniqueToUserCallback = async (args, token) =>
                !await db.UserPasskeys.AnyAsync(p => p.CredentialId == Base64Url.EncodeToString(args.CredentialId), token),
        }, ct);

        var passkey = new UserPasskey
        {
            UserId = user.Id,
            CredentialId = Base64Url.EncodeToString(credential.Id),
            PublicKey = Convert.ToBase64String(credential.PublicKey),
            SignCount = credential.SignCount,
            AaGuid = credential.AaGuid,
            Name = string.IsNullOrWhiteSpace(name)
                ? null
                : name.Trim()[..Math.Min(name.Trim().Length, UserPasskey.NameMaxLength)],
        };

        db.UserPasskeys.Add(passkey);
        await db.SaveChangesAsync(ct);

        return passkey;
    }

    /// <summary>
    /// Begins a sign-in ceremony.
    ///
    /// No email is asked for and no credential list is sent. That is discoverable-credential
    /// ("usernameless") sign-in, and it is also the privacy-preserving choice: returning the
    /// credential ids for an address would turn this endpoint into an account-existence oracle.
    /// </summary>
    public async Task<(string Handle, AssertionOptions Options)> BeginAssertionAsync(CancellationToken ct)
    {
        var options = fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = [],
            UserVerification = UserVerificationRequirement.Preferred,
        });

        var handle = await StoreChallengeAsync(options.ToJson(), null, ct);
        return (handle, options);
    }

    /// <summary>
    /// Completes a sign-in ceremony and returns the account it proves, or null.
    ///
    /// Every failure returns null rather than a reason. The caller is anonymous, and the
    /// difference between "no such credential" and "bad signature" is information about which
    /// credential ids exist.
    /// </summary>
    public async Task<User?> CompleteAssertionAsync(
        string handle, AuthenticatorAssertionRawResponse response, CancellationToken ct)
    {
        var challenge = await ConsumeChallengeAsync(handle, ct);
        if (challenge is null)
            return null;

        // Already base64url on the wire — re-encoding it would double-encode.
        var credentialId = response.Id;
        var passkey = await db.UserPasskeys.FirstOrDefaultAsync(p => p.CredentialId == credentialId, ct);
        if (passkey is null)
            return null;

        var options = AssertionOptions.FromJson(challenge.OptionsJson);

        try
        {
            var result = await fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = response,
                OriginalOptions = options,
                StoredPublicKey = Convert.FromBase64String(passkey.PublicKey),
                StoredSignatureCounter = passkey.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = (args, token) =>
                    Task.FromResult(new Guid(args.UserHandle) == passkey.UserId),
            }, ct);

            passkey.SignCount = result.SignCount;
            passkey.LastUsedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is Fido2VerificationException or FormatException or ArgumentException)
        {
            return null;
        }

        return await db.Users.FirstOrDefaultAsync(u => u.Id == passkey.UserId, ct);
    }

    /// <summary>Removes a passkey. Scoped by account, never by credential id alone.</summary>
    public async Task<bool> RemoveAsync(Guid userId, Guid passkeyId, CancellationToken ct)
    {
        var passkey = await db.UserPasskeys.FirstOrDefaultAsync(p => p.Id == passkeyId && p.UserId == userId, ct);
        if (passkey is null)
            return false;

        db.UserPasskeys.Remove(passkey);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>The relying-party origin, derived from the configured base URL.</summary>
    public string Origin => emailOptions.Value.BaseUrl.TrimEnd('/');

    private async Task<string> StoreChallengeAsync(string optionsJson, Guid? userId, CancellationToken ct)
    {
        var handle = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        db.PasskeyChallenges.Add(new PasskeyChallenge
        {
            Id = Guid.CreateVersion7(),
            Handle = handle,
            OptionsJson = optionsJson,
            UserId = userId,
            ExpiresAt = DateTime.UtcNow.Add(PasskeyChallenge.Lifetime),
        });

        await db.SaveChangesAsync(ct);
        return handle;
    }

    /// <summary>
    /// Reads a challenge and deletes it in the same breath.
    ///
    /// Single-use is the point: a challenge that survives its ceremony is a replay waiting to be
    /// noticed by somebody else.
    /// </summary>
    private async Task<PasskeyChallenge?> ConsumeChallengeAsync(string handle, CancellationToken ct)
    {
        var challenge = await db.PasskeyChallenges.FirstOrDefaultAsync(c => c.Handle == handle, ct);
        if (challenge is null)
            return null;

        db.PasskeyChallenges.Remove(challenge);
        await db.SaveChangesAsync(ct);

        return challenge.ExpiresAt > DateTime.UtcNow ? challenge : null;
    }
}
