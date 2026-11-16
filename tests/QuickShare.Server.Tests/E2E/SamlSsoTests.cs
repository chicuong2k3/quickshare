using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Auth.Saml;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// SAML 2.0 sign-in (issue #139).
///
/// These tests mint their own identity provider: a self-signed certificate, a real
/// <see cref="Saml2AuthnResponse"/> signed with it, POSTed to the assertion consumer exactly as
/// Okta would. That matters more here than anywhere else in the suite, because the thing being
/// tested is a *signature check* — and a signature check can only be shown to work by handing it
/// something that should fail.
///
/// So the shape of every negative test is the same: take a response that works, change one thing
/// an attacker controls, and require a rejection.
///
/// - signed by a different key → refused (otherwise any certificate is as good as the right one)
/// - issued by a different entity → refused (otherwise one tenant's IdP signs into another's)
/// - replayed verbatim → refused (a valid assertion sent twice is a stolen one)
/// - email outside the allowed domains → refused
/// </summary>
public sealed class SamlSsoTests : IClassFixture<TestWebAppFactory>
{
    private const string IdpSsoUrl = "https://idp.example/sso";
    private const string AcsPath = "/saml/acs";

    private readonly TestWebAppFactory _factory;

    public SamlSsoTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Metadata_IsServedAnonymously()
    {
        // An IdP admin has to fetch this before any binding exists, so it cannot be behind auth.
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/saml/metadata");
        var xml = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("EntityDescriptor", xml, StringComparison.Ordinal);
        Assert.Contains(AcsPath, xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_RedirectsToTheIdentityProvider()
    {
        var (admin, workspaceId) = await WorkspaceClientAsync();
        using var idp = NewIdentityProvider();
        await BindAsync(admin, IssuerFor(nameof(Login_RedirectsToTheIdentityProvider)), idp);

        var client = NoRedirectClient();
        var response = await client.GetAsync($"/saml/login/{workspaceId}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(IdpSsoUrl, response.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_IsNotFoundForAWorkspaceWithNoBinding()
    {
        var (_, workspaceId) = await WorkspaceClientAsync();

        var response = await NoRedirectClient().GetAsync($"/saml/login/{workspaceId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Acs_AcceptsASignedAssertionAndProvisionsASeat()
    {
        var issuer = IssuerFor(nameof(Acs_AcceptsASignedAssertionAndProvisionsASeat));
        var email = $"sam-{Guid.NewGuid():N}@saml.example";
        var (admin, workspaceId) = await WorkspaceClientAsync();
        using var idp = NewIdentityProvider();
        await BindAsync(admin, issuer, idp);

        var response = await PostAssertionAsync(BuildResponse(issuer, idp, email));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seat = await db.WorkspaceMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.InviteEmail == email);

        Assert.NotNull(seat);
        Assert.True(seat.IsActive);
    }

    [Fact]
    public async Task Acs_RefusesAnAssertionSignedByADifferentKey()
    {
        var issuer = IssuerFor(nameof(Acs_RefusesAnAssertionSignedByADifferentKey));
        var email = $"impostor-{Guid.NewGuid():N}@saml.example";
        var (admin, workspaceId) = await WorkspaceClientAsync();
        using var configured = NewIdentityProvider();
        using var attacker = NewIdentityProvider();
        await BindAsync(admin, issuer, configured);

        // Everything is right except the key. If this passed, the certificate the admin pasted in
        // would be decoration and anyone able to reach the ACS could sign themselves in.
        var response = await PostAssertionAsync(BuildResponse(issuer, attacker, email));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoSeatAsync(workspaceId, email);
    }

    [Fact]
    public async Task Acs_RefusesAnAssertionFromADifferentIssuer()
    {
        var issuer = IssuerFor(nameof(Acs_RefusesAnAssertionFromADifferentIssuer));
        var email = $"crosstenant-{Guid.NewGuid():N}@saml.example";
        var (admin, workspaceId) = await WorkspaceClientAsync();
        using var idp = NewIdentityProvider();
        await BindAsync(admin, issuer, idp);

        // Correctly signed by the configured key, but claiming to be a different identity
        // provider. Issuer and certificate are only meaningful checked as a pair.
        var response = await PostAssertionAsync(BuildResponse($"{issuer}-other", idp, email));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoSeatAsync(workspaceId, email);
    }

    [Fact]
    public async Task Acs_RefusesAReplayedAssertion()
    {
        var issuer = IssuerFor(nameof(Acs_RefusesAReplayedAssertion));
        var email = $"replay-{Guid.NewGuid():N}@saml.example";
        var (admin, _) = await WorkspaceClientAsync();
        using var idp = NewIdentityProvider();
        await BindAsync(admin, issuer, idp);

        var assertion = BuildResponse(issuer, idp, email);

        Assert.Equal(HttpStatusCode.Redirect, (await PostAssertionAsync(assertion)).StatusCode);

        // Byte-identical, still in its validity window, still correctly signed — and still an
        // attack. The signature and the clock both say yes; only the consumed-id record says no.
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAssertionAsync(assertion)).StatusCode);
    }

    [Fact]
    public async Task Acs_RefusesAnEmailOutsideTheAllowedDomains()
    {
        var issuer = IssuerFor(nameof(Acs_RefusesAnEmailOutsideTheAllowedDomains));
        var email = $"outsider-{Guid.NewGuid():N}@elsewhere.example";
        var (admin, workspaceId) = await WorkspaceClientAsync();
        using var idp = NewIdentityProvider();
        await BindAsync(admin, issuer, idp, domains: "saml.example");

        var response = await PostAssertionAsync(BuildResponse(issuer, idp, email));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoSeatAsync(workspaceId, email);
    }

    [Fact]
    public async Task Acs_RefusesJunk()
    {
        // No SAMLResponse at all. The ACS is an unauthenticated endpoint reachable by anyone.
        var response = await _factory.CreateClient().PostAsync(AcsPath,
            new FormUrlEncodedContent([new KeyValuePair<string, string>("SAMLResponse", "not-base64-xml")]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Binding_RefusesACertificateItCannotParse()
    {
        var (admin, _) = await WorkspaceClientAsync();

        var response = await admin.PutAsJsonAsync("/api/workspace/sso", new
        {
            provider = SsoProvider.Saml,
            directoryId = IssuerFor(nameof(Binding_RefusesACertificateItCannotParse)),
            samlSsoUrl = IdpSsoUrl,
            samlCertificate = "this is not a certificate",
        });

        // Refused at configuration time rather than at 3am on the first sign-in attempt.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void Certificate_ParsesWithOrWithoutPemArmour()
    {
        using var idp = NewIdentityProvider();
        var bare = Convert.ToBase64String(idp.Export(X509ContentType.Cert));
        var armoured = $"-----BEGIN CERTIFICATE-----\n{bare}\n-----END CERTIFICATE-----";

        // Both forms come out of real IdP admin consoles; refusing one produces a support ticket,
        // not a more secure deployment.
        Assert.True(SamlService.IsUsableCertificate(bare));
        Assert.True(SamlService.IsUsableCertificate(armoured));
        Assert.False(SamlService.IsUsableCertificate("nonsense"));
    }

    [Fact]
    public void ReadEmail_IgnoresAnOpaqueNameId()
    {
        // A transient NameID is a pairwise pseudonym. Treating one as an address would create an
        // account under a string its owner has never seen and cannot receive mail at.
        var opaque = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "AAdzZWNyZXQ")]);
        Assert.Null(SamlService.ReadEmail(opaque));

        var addressed = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "sam@saml.example")]);
        Assert.Equal("sam@saml.example", SamlService.ReadEmail(addressed));
    }

    // ── Helpers ──

    /// <summary>A distinct entity id per test: a directory is claimable by one workspace (#181).</summary>
    private static string IssuerFor(string test) => $"https://idp.example/{test}/{Guid.NewGuid():N}";

    private static X509Certificate2 NewIdentityProvider()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Test IdP", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        // Round-tripped through PKCS#12 so the private key is usable for signing on Windows.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null,
            X509KeyStorageFlags.Exportable);
    }

    private HttpClient NoRedirectClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    private async Task<(HttpClient Client, Guid WorkspaceId)> WorkspaceClientAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = $"SAML {Guid.NewGuid():N}"[..16], Seats = 5 });
        response.EnsureSuccessStatusCode();
        var workspace = (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;

        return (client, workspace.Id);
    }

    private static async Task BindAsync(HttpClient admin, string issuer, X509Certificate2 idp, string? domains = null)
    {
        var response = await admin.PutAsJsonAsync("/api/workspace/sso", new
        {
            provider = SsoProvider.Saml,
            directoryId = issuer,
            samlSsoUrl = IdpSsoUrl,
            samlCertificate = Convert.ToBase64String(idp.Export(X509ContentType.Cert)),
            allowedEmailDomains = domains,
            enabled = true,
            jitRole = nameof(WorkspaceRole.Member),
        });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Mints a signed SAML response the way an identity provider would. The audience and
    /// destination have to match what the service provider advertises, or the library refuses it
    /// for the wrong reason and the negative tests stop proving anything.
    /// </summary>
    private string BuildResponse(string issuer, X509Certificate2 signingCertificate, string email)
    {
        var entityId = ServiceProviderEntityId();

        var config = new Saml2Configuration
        {
            Issuer = issuer,
            SigningCertificate = signingCertificate,
        };
        config.AllowedAudienceUris.Add(entityId);

        var response = new Saml2AuthnResponse(config)
        {
            Status = Saml2StatusCodes.Success,
            Destination = new Uri($"{BaseUrl()}{AcsPath}"),
        };

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, email),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, "SAML Tester"),
        ]);

        response.NameId = new Microsoft.IdentityModel.Tokens.Saml2.Saml2NameIdentifier(email, NameIdentifierFormats.Email);
        response.ClaimsIdentity = identity;
        response.CreateSecurityToken(entityId, subjectConfirmationLifetime: 10, issuedTokenLifetime: 10);

        var binding = new Saml2PostBinding();
        binding.Bind(response);

        return binding.PostContent is null
            ? throw new InvalidOperationException("binding produced no content")
            : ExtractSamlResponse(binding.PostContent);
    }

    /// <summary>Pulls the base64 SAMLResponse out of the auto-post form the binding renders.</summary>
    private static string ExtractSamlResponse(string postContent)
    {
        const string Marker = "name=\"SAMLResponse\" value=\"";
        var start = postContent.IndexOf(Marker, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException("no SAMLResponse in post content");

        start += Marker.Length;
        var end = postContent.IndexOf('"', start);
        return System.Net.WebUtility.HtmlDecode(postContent[start..end]);
    }

    private Task<HttpResponseMessage> PostAssertionAsync(string samlResponse) =>
        NoRedirectClient().PostAsync(AcsPath, new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("SAMLResponse", samlResponse)]));

    private string BaseUrl()
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<QuickShare.Infrastructure.Email.EmailOptions>>();
        return options.Value.BaseUrl.TrimEnd('/');
    }

    private string ServiceProviderEntityId() => $"{BaseUrl()}/saml/metadata";

    private async Task AssertNoSeatAsync(Guid workspaceId, string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(m => m.WorkspaceId == workspaceId && m.InviteEmail == email));
    }
}
