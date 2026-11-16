using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Security.Claims;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using ITfoxtec.Identity.Saml2.Schemas.Metadata;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Caching;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Auth.Saml;

/// <summary>
/// SAML 2.0 service-provider plumbing (issue #139).
///
/// The signature validation, canonicalisation and assertion parsing are ITfoxtec's, not ours.
/// That is the whole reason this batch waited: XML digital signatures are a field with a long
/// history of implementations that validate *a* signature rather than the signature over the
/// element they then read, and hand-rolling that is how an SSO integration becomes a bypass.
///
/// What lives here is everything the library cannot know: which workspace an assertion belongs
/// to, whether its issuer is one we accept, and whether we have seen this assertion before.
/// </summary>
public sealed class SamlService(
    AppDbContext db,
    IOptions<EmailOptions> emailOptions,
    ILogger<SamlService> logger)
{
    /// <summary>Claim types Okta, Entra and Google all emit for an address, in preference order.</summary>
    private static readonly string[] EmailClaimTypes =
    [
        ClaimTypes.Email,
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress",
        "urn:oid:1.2.840.113549.1.9.1",
        "email",
        "mail",
    ];

    /// <summary>Group/role claim types, same idea. Entra emits `groups`, Okta usually `groups`.</summary>
    private static readonly string[] GroupClaimTypes =
    [
        ClaimTypes.Role,
        "http://schemas.microsoft.com/ws/2008/06/identity/claims/groups",
        "groups",
        "Groups",
        "memberOf",
    ];

    /// <summary>
    /// A replayed assertion is a stolen one. The window is short because an assertion carries its
    /// own <c>NotOnOrAfter</c>; once that has passed the library refuses it anyway, so the cache
    /// only has to cover the assertion's own lifetime.
    /// </summary>
    private static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(15);

    private string BaseUrl => emailOptions.Value.BaseUrl.TrimEnd('/');

    /// <summary>This deployment's SAML entity id. One SP, many workspace bindings.</summary>
    public string EntityId => $"{BaseUrl}/saml/metadata";

    /// <summary>Where the identity provider POSTs its assertion.</summary>
    public string AssertionConsumerUrl => $"{BaseUrl}{SamlRoutes.Acs}";

    /// <summary>
    /// Builds the library configuration for one workspace binding.
    ///
    /// <c>AllowedIssuer</c> is the load-bearing line: without it a validly signed assertion from
    /// *any* identity provider whose certificate we happen to trust would be accepted for this
    /// workspace. Issuer and certificate have to be checked as a pair, or the pairing is
    /// decorative.
    /// </summary>
    public Saml2Configuration BuildConfiguration(WorkspaceSsoConfig binding)
    {
        var config = new Saml2Configuration
        {
            Issuer = EntityId,
            AllowedIssuer = binding.DirectoryId,
            SingleSignOnDestination = new Uri(binding.SamlSsoUrl!),
            // Revocation checking would make sign-in depend on reaching a CRL endpoint, and IdP
            // signing certificates are routinely self-signed — the trust anchor is the exact
            // certificate the admin pasted in, not a chain.
            // Revocation checking would make sign-in depend on reaching a CRL endpoint, and IdP
            // signing certificates are routinely self-signed — the trust anchor is the exact
            // certificate the admin pasted in, not a chain back to a public root.
            RevocationMode = X509RevocationMode.NoCheck,
            // And no chain validation. An identity provider's signing certificate is routinely
            // self-signed — Okta and Entra both hand you one — so requiring a path to a public
            // root would reject the normal case. The trust anchor here is the exact certificate
            // the admin pasted in, which is a stronger statement than "some CA vouched for it".
            CertificateValidationMode = System.ServiceModel.Security.X509CertificateValidationMode.None,
        };

        config.SignatureValidationCertificates.Add(ParseCertificate(binding.SamlCertificate!));
        config.AllowedAudienceUris.Add(EntityId);

        return config;
    }

    /// <summary>SP metadata, for pasting into the identity provider.</summary>
    public string BuildMetadata()
    {
        var config = new Saml2Configuration { Issuer = EntityId };

        var entity = new EntityDescriptor(config)
        {
            ValidUntil = (int)TimeSpan.FromDays(30).TotalDays,
            SPSsoDescriptor = new SPSsoDescriptor
            {
                WantAssertionsSigned = true,
                AssertionConsumerServices =
                [
                    new AssertionConsumerService { Binding = ProtocolBindings.HttpPost, Location = new Uri(AssertionConsumerUrl) },
                ],
                NameIDFormats = [NameIdentifierFormats.Email],
            },
        };

        return new Saml2Metadata(entity).CreateMetadata().ToXml();
    }

    /// <summary>The workspace binding for an identity provider entity id, or null.</summary>
    public Task<WorkspaceSsoConfig?> FindByIssuerAsync(string issuer, CancellationToken ct) =>
        db.WorkspaceSsoConfigs.FirstOrDefaultAsync(
            c => c.Enabled && c.Provider == SsoProvider.Saml && c.DirectoryId == issuer, ct);

    public Task<WorkspaceSsoConfig?> FindByWorkspaceAsync(Guid workspaceId, CancellationToken ct) =>
        db.WorkspaceSsoConfigs.FirstOrDefaultAsync(
            c => c.Enabled && c.Provider == SsoProvider.Saml && c.WorkspaceId == workspaceId, ct);

    /// <summary>
    /// Records an assertion id and reports whether it had already been used.
    ///
    /// The library validates the signature and the time window; neither notices the *same*
    /// correctly-signed assertion arriving twice, which is exactly what an attacker who
    /// intercepted one would send.
    /// </summary>
    public async Task<bool> IsReplayAsync(string assertionId, CancellationToken ct)
    {
        // An assertion with no id cannot be tracked, so it cannot be trusted either.
        if (string.IsNullOrEmpty(assertionId) || assertionId.Length > SamlAssertionSeen.AssertionIdMaxLength)
            return true;

        db.SamlAssertionsSeen.Add(new SamlAssertionSeen
        {
            Id = Guid.CreateVersion7(),
            AssertionId = assertionId,
            ExpiresAt = DateTime.UtcNow.Add(ReplayWindow),
        });

        try
        {
            await db.SaveChangesAsync(ct);
            return false;
        }
        catch (DbUpdateException)
        {
            // The unique index rejected it: this assertion has been consumed already. Letting the
            // database decide keeps the check atomic across instances, which a read-then-write
            // could not be.
            db.ChangeTracker.Clear();
            return true;
        }
    }

    /// <summary>
    /// Reads the <c>Issuer</c> out of a SAML response **without validating anything**.
    ///
    /// This exists to answer one question — which workspace binding, and therefore which
    /// certificate, this assertion should be checked against — and there is no way to answer it
    /// before reading the assertion. The return value is attacker-controlled and is used for
    /// exactly one thing: a database lookup. If it names a binding, the assertion still has to
    /// survive that binding's signature check; if it lies, it selects a certificate that will not
    /// verify it.
    ///
    /// XML parsing is locked down: no DTD, no resolver, so this cannot be turned into an XXE
    /// against the one place we touch untrusted XML ourselves.
    /// </summary>
    public static string? TryReadIssuer(string? samlResponseBase64)
    {
        if (string.IsNullOrWhiteSpace(samlResponseBase64))
            return null;

        try
        {
            var xml = Encoding.UTF8.GetString(Convert.FromBase64String(samlResponseBase64));

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
            };

            using var stringReader = new StringReader(xml);
            using var reader = XmlReader.Create(stringReader, settings);

            var document = new XmlDocument { XmlResolver = null };
            document.Load(reader);

            // The response-level Issuer, not an assertion-level one: the binding is chosen by who
            // sent the message.
            var issuer = document.DocumentElement?
                .GetElementsByTagName("Issuer", "urn:oasis:names:tc:SAML:2.0:assertion");

            return issuer is { Count: > 0 } ? issuer[0]!.InnerText.Trim() : null;
        }
        catch (Exception ex) when (ex is FormatException or XmlException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The address the assertion asserts, from whichever claim the provider chose.</summary>
    public static string? ReadEmail(ClaimsIdentity identity)
    {
        foreach (var type in EmailClaimTypes)
        {
            var value = identity.FindFirst(type)?.Value;
            if (!string.IsNullOrWhiteSpace(value) && value.Contains('@', StringComparison.Ordinal))
                return value.Trim().ToLowerInvariant();
        }

        // Falling back to the NameID only when it is an address: a transient or persistent
        // NameID is an opaque pairwise identifier, and treating one as an email would create an
        // account under a string the person has never seen.
        var nameId = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return !string.IsNullOrWhiteSpace(nameId) && nameId.Contains('@', StringComparison.Ordinal)
            ? nameId.Trim().ToLowerInvariant()
            : null;
    }

    /// <summary>Group values the assertion carries, for the admin-group mapping.</summary>
    public static IReadOnlyList<string> ReadGroups(ClaimsIdentity identity) =>
        [.. identity.Claims
            .Where(c => GroupClaimTypes.Contains(c.Type, StringComparer.OrdinalIgnoreCase))
            .Select(c => c.Value)];

    /// <summary>
    /// Parses a pasted signing certificate. Accepts PEM with or without its armour, which is what
    /// people actually get out of Okta and Entra — refusing one form would produce a support
    /// ticket rather than a more secure deployment.
    /// </summary>
    public static X509Certificate2 ParseCertificate(string value)
    {
        var cleaned = value
            .Replace("-----BEGIN CERTIFICATE-----", string.Empty, StringComparison.Ordinal)
            .Replace("-----END CERTIFICATE-----", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();

        return X509CertificateLoader.LoadCertificate(Convert.FromBase64String(cleaned));
    }

    /// <summary>True when a pasted certificate is something we can actually validate against.</summary>
    public static bool IsUsableCertificate(string value)
    {
        try
        {
            using var certificate = ParseCertificate(value);
            return certificate.PublicKey is not null;
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    public void LogRejected(string reason, string? issuer) =>
        logger.LogWarning("SAML assertion rejected: {Reason} (issuer {Issuer})", reason, issuer);
}

/// <summary>SAML endpoint paths. Fixed strings — an identity provider is configured with them.</summary>
public static class SamlRoutes
{
    public const string Metadata = "/saml/metadata";
    public const string Acs = "/saml/acs";
    public const string Login = "/saml/login";
}
