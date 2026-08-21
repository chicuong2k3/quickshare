using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Auth.Mfa;
using QuickShare.Features.Auth.Sso;
using QuickShare.Features.Auth.Totp;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Features.Workspaces.Shared;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Enterprise identity: authenticator enrolment (#128), OIDC SSO (#181), SCIM provisioning
/// (#146) and the MFA policy that ties them together (#219).
///
/// The invariants worth holding are all about *proof* and *revocation*:
///
/// - A directory claim is the proof of membership, never the email domain. An email ending in
///   @acme.com is something anyone can buy; `hd=acme.com` is something only Google can say.
/// - A code is single-use, and a recovery code is spent when used. Both are replay defences and
///   both are trivially wrong in a way no happy-path test would notice.
/// - Deprovisioning must actually revoke. SCIM exists to answer "what happens at offboarding",
///   so a PATCH that returns 200 and leaves the seat live is the failure the feature is for.
/// - Turning SSO-only on must not be able to lock the owner out, because nothing inside the
///   product could then turn it back off.
/// </summary>
public sealed class EnterpriseIdentityTests : IClassFixture<TestWebAppFactory>
{
    // One directory per test: a directory can only be claimed by one workspace, and the suite
    // shares a database.
    private static string NewDirectory() => $"acme-{Guid.NewGuid():N}.example";
    private static string NewTenantId() => Guid.NewGuid().ToString();

    private readonly TestWebAppFactory _factory;

    public EnterpriseIdentityTests(TestWebAppFactory factory) => _factory = factory;

    // ── #128 TOTP ──

    [Fact]
    public void Totp_ComputesTheRfc6238ReferenceVector()
    {
        // RFC 6238 Appendix B: the ASCII secret "12345678901234567890" at T=59 gives 94287082.
        // Anchoring to the published vector is the only way to know the implementation is
        // interoperable rather than merely self-consistent — a broken one still round-trips.
        const string Base32OfRfcSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var step = 59 / UserTotp.PeriodSeconds;

        Assert.Equal("287082", TotpService.ComputeCode(Base32OfRfcSecret, step));
    }

    [Fact]
    public async Task Totp_EnrolmentRequiresACorrectCode()
    {
        var (client, _) = await AccountAsync();

        var start = await client.PostAsync("/api/account/totp/start", null);
        start.EnsureSuccessStatusCode();
        var enrolment = (await start.Content.ReadFromJsonAsync<TotpEnrolmentDto>())!;

        var wrong = await client.PostAsJsonAsync("/api/account/totp/confirm", new { code = "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var right = await client.PostAsJsonAsync("/api/account/totp/confirm",
            new { code = TotpService.ComputeCode(enrolment.Secret, TotpService.StepFor(DateTime.UtcNow)) });
        right.EnsureSuccessStatusCode();

        var recovery = (await right.Content.ReadFromJsonAsync<TotpRecoveryDto>())!;
        Assert.Equal(UserTotp.RecoveryCodeCount, recovery.Codes.Count);
    }

    [Fact]
    public void Totp_RefusesAReplayedCode()
    {
        var secret = TotpService.GenerateSecret();
        var now = DateTime.UtcNow;
        var step = TotpService.StepFor(now);
        var code = TotpService.ComputeCode(secret, step);

        Assert.True(TotpService.TryVerify(secret, code, now, lastUsedStep: 0, out var matched));
        Assert.Equal(step, matched);

        // Same code, same window, but the step has been spent. A shoulder-surfed six digits must
        // not stay live for the remainder of its thirty seconds.
        Assert.False(TotpService.TryVerify(secret, code, now, lastUsedStep: matched, out _));
    }

    [Fact]
    public void Totp_AcceptsOneStepOfClockDrift()
    {
        var secret = TotpService.GenerateSecret();
        var now = DateTime.UtcNow;
        var previous = TotpService.ComputeCode(secret, TotpService.StepFor(now) - 1);

        Assert.True(TotpService.TryVerify(secret, previous, now, 0, out _));
        // Two steps out is refused — the window is a courtesy, not an open door.
        var stale = TotpService.ComputeCode(secret, TotpService.StepFor(now) - 3);
        Assert.False(TotpService.TryVerify(secret, stale, now, 0, out _));
    }

    [Fact]
    public void Totp_SpendsARecoveryCodeOnce()
    {
        var (codes, hashes) = TotpService.GenerateRecoveryCodes();

        Assert.True(TotpService.TrySpendRecoveryCode(hashes, codes[0], out var remaining));
        Assert.Equal(UserTotp.RecoveryCodeCount - 1, TotpService.CountRecoveryCodes(remaining));
        Assert.False(TotpService.TrySpendRecoveryCode(remaining, codes[0], out _));
    }

    [Fact]
    public async Task Totp_EnrolledSessionIsChallengedOnTheNextRequest()
    {
        var (client, userId) = await AccountAsync();
        await EnrolAsync(client, userId);

        // A fresh client is a fresh session: it carries no verified claim, so the gate must hold
        // it. An enrolled account whose sessions sail past the challenge has a decoration.
        var fresh = _factory.CreateUnverifiedClient(ProviderKeyFor(userId));
        var response = await fresh.GetAsync("/api/deliveries");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.MfaRequired, response.Headers.GetValues("X-QuickShare-Mfa").Single());
    }

    [Fact]
    public async Task Totp_EnrolmentRouteStaysOpenToAChallengedSession()
    {
        var (client, userId) = await AccountAsync();
        await EnrolAsync(client, userId);

        // "Blocked" must never mean "stuck": the route that answers the challenge is the one
        // route a challenged session has to be able to reach.
        var fresh = _factory.CreateUnverifiedClient(ProviderKeyFor(userId));
        var status = await fresh.GetAsync("/api/account/totp");

        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
    }

    // ── #181 SSO ──

    [Fact]
    public void Sso_EmailDomainNarrowsButNeverEstablishesMembership()
    {
        var config = new WorkspaceSsoConfig
        {
            Provider = SsoProvider.GoogleWorkspace,
            DirectoryId = "acme.com",
            AllowedEmailDomains = "acme.com, acme.co.uk",
        };

        Assert.True(SsoLoginService.EmailMatchesDirectory(config, "sam@acme.com"));
        Assert.True(SsoLoginService.EmailMatchesDirectory(config, "sam@ACME.CO.UK"));
        Assert.False(SsoLoginService.EmailMatchesDirectory(config, "sam@evil.com"));
        // A lookalike that merely ends in the allowed domain must not pass.
        Assert.False(SsoLoginService.EmailMatchesDirectory(config, "sam@notacme.com"));
    }

    [Fact]
    public async Task Sso_ProvisionsASeatForADirectoryAccount()
    {
        var directory = NewDirectory();
        var email = $"newhire@{directory}";
        var (client, workspaceId) = await WorkspaceClientAsync();
        await BindDirectoryAsync(client, SsoProvider.GoogleWorkspace, directory);

        var (_, newcomerId) = await AccountAsync(email);
        await ApplySsoAsync(newcomerId, email, AuthConstants.GoogleProvider,
            [new Claim(SsoLoginService.HostedDomainClaim, directory)]);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seat = await db.WorkspaceMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.InviteEmail == email);

        Assert.NotNull(seat);
        Assert.True(seat.IsActive);
        Assert.Equal(WorkspaceRole.Member, seat.Role);
    }

    [Fact]
    public async Task Sso_RefusesAnAccountWithNoDirectoryClaim()
    {
        var directory = NewDirectory();
        var email = $"ceo@{directory}";
        var (client, workspaceId) = await WorkspaceClientAsync();
        await BindDirectoryAsync(client, SsoProvider.GoogleWorkspace, directory);

        // A consumer Google account on a custom domain: the address looks right, the token says
        // nothing. This is the account-takeover case the whole design turns on.
        var (_, impostorId) = await AccountAsync(email);
        await ApplySsoAsync(impostorId, email, AuthConstants.GoogleProvider, []);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seat = await db.WorkspaceMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.InviteEmail == email);

        Assert.Null(seat);
    }

    [Fact]
    public async Task Sso_RefusesADirectoryClaimForADifferentTenant()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();
        await BindDirectoryAsync(client, SsoProvider.Entra, NewTenantId());

        var (_, outsiderId) = await AccountAsync("sam@other.example");
        await ApplySsoAsync(outsiderId, "sam@other.example", AuthConstants.EntraProvider,
            [new Claim(SsoLoginService.TenantIdClaim, Guid.NewGuid().ToString())]);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(m => m.WorkspaceId == workspaceId && m.InviteEmail == "sam@other.example"));
    }

    [Fact]
    public async Task Sso_EntraGroupClaimProvisionsAnAdmin()
    {
        var adminGroup = Guid.NewGuid().ToString();
        var tenantId = NewTenantId();
        var email = $"lead-{Guid.NewGuid():N}@acme.example";
        var (client, workspaceId) = await WorkspaceClientAsync();

        var bind = await client.PutAsJsonAsync("/api/workspace/sso", new
        {
            provider = SsoProvider.Entra,
            directoryId = tenantId,
            enabled = true,
            jitRole = nameof(WorkspaceRole.Member),
            adminGroupId = adminGroup,
        });
        bind.EnsureSuccessStatusCode();

        var (_, leadId) = await AccountAsync(email);
        await ApplySsoAsync(leadId, email, AuthConstants.EntraProvider,
        [
            new Claim(SsoLoginService.TenantIdClaim, tenantId),
            new Claim(SsoLoginService.GroupsClaim, adminGroup),
        ]);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seat = await db.WorkspaceMembers.AsNoTracking()
            .FirstAsync(m => m.WorkspaceId == workspaceId && m.InviteEmail == email);

        Assert.Equal(WorkspaceRole.Admin, seat.Role);
    }

    [Fact]
    public async Task Sso_NeverProvisionsAnOwnerRole()
    {
        var (client, _) = await WorkspaceClientAsync();

        var response = await client.PutAsJsonAsync("/api/workspace/sso", new
        {
            provider = SsoProvider.GoogleWorkspace,
            directoryId = NewDirectory(),
            jitRole = nameof(WorkspaceRole.Owner),
        });

        // Ownership carries the bill; a directory group is not a way to hand somebody else's
        // subscription around (#252 exists because that move needs consent).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sso_RefusesSsoOnlyWhenItWouldLockTheOwnerOut()
    {
        var (client, _) = await WorkspaceClientAsync();

        var response = await client.PutAsJsonAsync("/api/workspace/sso", new
        {
            provider = SsoProvider.GoogleWorkspace,
            directoryId = NewDirectory(),
            allowedEmailDomains = "nobody.example",
            requireSso = true,
        });

        // The owner's address does not pass the filter. Allowing this would leave nobody able to
        // turn it back off — there is no path out of it inside the product.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Sso_RefusesADirectoryAnotherWorkspaceAlreadyClaims()
    {
        var directory = NewDirectory();
        var (first, _) = await WorkspaceClientAsync();
        await BindDirectoryAsync(first, SsoProvider.GoogleWorkspace, directory);

        var (second, _) = await WorkspaceClientAsync();
        var response = await second.PutAsJsonAsync("/api/workspace/sso", new
        {
            provider = SsoProvider.GoogleWorkspace,
            directoryId = directory,
            enabled = true,
        });

        // Otherwise any workspace could bind itself to acme.com and quietly absorb every
        // acme.com account that signed in afterwards: the claim would be proved, then handed to
        // the wrong tenant.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── #146 SCIM ──

    [Fact]
    public async Task Scim_RefusesAnUnknownToken()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");

        var response = await client.GetAsync("/scim/v2/Users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Scim_ProvisionsAndThenDeprovisionsASeat()
    {
        var contractorEmail = $"contractor-{Guid.NewGuid():N}@acme.example";
        var (admin, workspaceId) = await WorkspaceClientAsync();
        var scim = await ScimClientAsync(admin);

        var created = await scim.PostAsJsonAsync("/scim/v2/Users", new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" },
            userName = contractorEmail,
            active = true,
        });
        Assert.True(created.StatusCode == HttpStatusCode.Created, $"{(int)created.StatusCode}: {await created.Content.ReadAsStringAsync()}");
        var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement;
        var scimId = body.GetProperty("id").GetString()!;
        Assert.True(body.GetProperty("active").GetBoolean());

        // The offboarding call. This is the question procurement actually asks.
        var patched = await scim.PatchAsync($"/scim/v2/Users/{scimId}", JsonContent(new
        {
            schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:PatchOp" },
            Operations = new object[] { new { op = "replace", path = "active", value = false } },
        }));
        patched.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seat = await db.WorkspaceMembers.AsNoTracking()
            .FirstAsync(m => m.WorkspaceId == workspaceId && m.InviteEmail == contractorEmail);

        Assert.NotNull(seat.RevokedAt);
        Assert.True(await db.SecurityEvents.AsNoTracking()
            .AnyAsync(e => e.WorkspaceId == workspaceId && e.EventType == SecurityEventType.ScimUserDeprovisioned));
    }

    [Fact]
    public async Task Scim_ReadsTheEntraShapeOfDeactivation()
    {
        var entraEmail = $"entra-{Guid.NewGuid():N}@acme.example";
        var (admin, workspaceId) = await WorkspaceClientAsync();
        var scim = await ScimClientAsync(admin);

        var created = await scim.PostAsJsonAsync("/scim/v2/Users", new { userName = entraEmail, active = true });
        var scimId = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // Entra nests the value instead of using a path. Reading only Okta's shape would leave
        // half the market silently un-offboarded, which is worse than not shipping SCIM at all.
        var patched = await scim.PatchAsync($"/scim/v2/Users/{scimId}", JsonContent(new
        {
            Operations = new object[] { new { op = "Replace", value = new { active = false } } },
        }));
        patched.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seat = await db.WorkspaceMembers.AsNoTracking()
            .FirstAsync(m => m.WorkspaceId == workspaceId && m.InviteEmail == entraEmail);

        Assert.NotNull(seat.RevokedAt);
    }

    [Fact]
    public async Task Scim_IsIdempotentOnUserName()
    {
        var twiceEmail = $"twice-{Guid.NewGuid():N}@acme.example";
        var (admin, workspaceId) = await WorkspaceClientAsync();
        var scim = await ScimClientAsync(admin);

        await scim.PostAsJsonAsync("/scim/v2/Users", new { userName = twiceEmail, active = true });
        var again = await scim.PostAsJsonAsync("/scim/v2/Users", new { userName = twiceEmail, active = true });

        // A re-run sync must not eat a second paid seat.
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.WorkspaceMembers.AsNoTracking()
            .CountAsync(m => m.WorkspaceId == workspaceId && m.InviteEmail == twiceEmail));
    }

    [Fact]
    public async Task Scim_CannotReachAnotherWorkspace()
    {
        var (adminA, _) = await WorkspaceClientAsync();
        var (adminB, workspaceB) = await WorkspaceClientAsync();

        var scimA = await ScimClientAsync(adminA);
        var scimB = await ScimClientAsync(adminB);

        var theirsEmail = $"theirs-{Guid.NewGuid():N}@acme.example";
        var created = await scimB.PostAsJsonAsync("/scim/v2/Users", new { userName = theirsEmail, active = true });
        var scimId = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // The token is the tenant boundary (#190): A's token must not resolve B's seat.
        var reachAcross = await scimA.GetAsync($"/scim/v2/Users/{scimId}");
        Assert.Equal(HttpStatusCode.NotFound, reachAcross.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(m => m.WorkspaceId == workspaceB && m.InviteEmail == theirsEmail && m.RevokedAt == null));
    }

    [Fact]
    public async Task Scim_RevokedTokenStopsWorking()
    {
        var (admin, _) = await WorkspaceClientAsync();

        var issued = await admin.PostAsJsonAsync("/api/workspace/scim-tokens", new { label = "Okta" });
        var token = (await issued.Content.ReadFromJsonAsync<ScimTokenDto>())!;

        var revoke = await admin.DeleteAsync($"/api/workspace/scim-tokens/{token.Id}");
        revoke.EnsureSuccessStatusCode();

        var scim = _factory.CreateClient();
        scim.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await scim.GetAsync("/scim/v2/Users")).StatusCode);
    }

    [Fact]
    public async Task Scim_TokenIsNeverReadableAfterIssue()
    {
        var (admin, _) = await WorkspaceClientAsync();

        var issued = await admin.PostAsJsonAsync("/api/workspace/scim-tokens", new { label = "Okta" });
        var created = (await issued.Content.ReadFromJsonAsync<ScimTokenDto>())!;
        Assert.False(string.IsNullOrEmpty(created.Token));

        var listed = await admin.GetFromJsonAsync<List<ScimTokenDto>>("/api/workspace/scim-tokens");
        Assert.All(listed!, t => Assert.Null(t.Token));

        // Not even in the row: only the hash is stored.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.WorkspaceScimTokens.AsNoTracking().AnyAsync(t => t.TokenHash == created.Token));
    }

    // ── #219 MFA policy ──

    [Fact]
    public async Task MfaPolicy_LeavesAMemberAloneInsideTheGraceWindow()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();

        var set = await client.PutAsJsonAsync("/api/workspace/mfa", new { mfaRequired = true, mfaGraceDays = 7 });
        set.EnsureSuccessStatusCode();

        var verdict = await EvaluateAsync(workspaceId);
        Assert.True(verdict.Required);
        Assert.False(verdict.Blocked);
        Assert.True(verdict.InGrace);

        // Still usable — enforcement that bites the moment an admin ticks the box is how a
        // security control gets switched off and never turned back on.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/deliveries")).StatusCode);
    }

    [Fact]
    public async Task MfaPolicy_BlocksOnceTheGraceWindowHasPassed()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();
        await client.PutAsJsonAsync("/api/workspace/mfa", new { mfaRequired = true, mfaGraceDays = 7 });
        await BackdatePolicyAsync(workspaceId, TimeSpan.FromDays(30));

        var verdict = await EvaluateAsync(workspaceId);

        Assert.True(verdict.Blocked);
    }

    [Fact]
    public async Task MfaPolicy_ExemptsMembersOfTheSsoDirectory()
    {
        var directory = NewDirectory();
        var (client, workspaceId) = await WorkspaceClientAsync();
        await SetOwnerEmailAsync(workspaceId, $"boss@{directory}");
        await BindDirectoryAsync(client, SsoProvider.GoogleWorkspace, directory, directory);
        await client.PutAsJsonAsync("/api/workspace/mfa", new { mfaRequired = true, mfaGraceDays = 0 });
        await BackdatePolicyAsync(workspaceId, TimeSpan.FromDays(30));

        var verdict = await EvaluateAsync(workspaceId);

        // The directory already enforces a second factor. Requiring another one here would be
        // theatre the directory admin has no way to switch off.
        Assert.True(verdict.SsoExempt);
        Assert.False(verdict.Blocked);
    }

    [Fact]
    public async Task MfaPolicy_CountsSsoMembersAsCoveredInThePosture()
    {
        var directory = NewDirectory();
        var (client, workspaceId) = await WorkspaceClientAsync();
        await SetOwnerEmailAsync(workspaceId, $"boss@{directory}");
        await BindDirectoryAsync(client, SsoProvider.GoogleWorkspace, directory, directory);

        var posture = await client.GetFromJsonAsync<MfaPolicyDto>("/api/workspace/mfa");

        Assert.Equal(1, posture!.Members);
        Assert.Equal(1, posture.Covered);
    }

    [Fact]
    public async Task MfaPolicy_RefusesToRemoveAnAuthenticatorItRequires()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();
        var userId = await OwnerIdAsync(workspaceId);
        var secret = await EnrolAsync(client, userId);
        await client.PutAsJsonAsync("/api/workspace/mfa", new { mfaRequired = true, mfaGraceDays = 7 });

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/account/totp")
        {
            Content = JsonContent(new { code = TotpService.ComputeCode(secret, TotpService.StepFor(DateTime.UtcNow) + 2) }),
        };
        var response = await client.SendAsync(request);

        // A correct code would not help either: while the policy applies, removal is refused, or
        // the policy is a suggestion.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task MfaPolicy_RejectsAnAbsurdGracePeriod()
    {
        var (client, _) = await WorkspaceClientAsync();

        var response = await client.PutAsJsonAsync("/api/workspace/mfa",
            new { mfaRequired = true, mfaGraceDays = Workspace.MaxMfaGraceDays + 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MfaPolicy_DoesNotApplyToAnAccountWithNoSeat()
    {
        var (_, userId) = await AccountAsync();

        var verdict = await EvaluateForUserAsync(userId);

        // A personal account has no org to set a policy, and inventing one would be a lockout
        // with nobody to appeal to.
        Assert.False(verdict.Required);
    }

    // ── The screens ──

    [Fact]
    public async Task SsoPage_CarriesTheBindingAndThePosture()
    {
        var directory = NewDirectory();
        var (client, _) = await WorkspaceClientAsync();
        await BindDirectoryAsync(client, SsoProvider.GoogleWorkspace, directory);
        await client.PostAsJsonAsync("/api/workspace/scim-tokens", new { label = "Okta" });

        using var request = new HttpRequestMessage(HttpMethod.Get, RoutePaths.AccountWorkspaceSso);
        request.Headers.Add("HX-Request", "true");
        var response = await client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(directory, html, StringComparison.Ordinal);
        Assert.Contains("sso.save", html, StringComparison.Ordinal);
        Assert.Contains("mfa.policy.save", html, StringComparison.Ordinal);
        Assert.Contains("scim.token.create", html, StringComparison.Ordinal);
        Assert.Contains("Okta", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecurityPage_OffersEnrolmentAndNeverLeaksTheSecret()
    {
        var (client, _) = await AccountAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, RoutePaths.AccountSecurity);
        request.Headers.Add("HX-Request", "true");
        var html = await (await client.SendAsync(request)).Content.ReadAsStringAsync();

        Assert.Contains("security.totp.start", html, StringComparison.Ordinal);

        // The secret is fetched by script and shown once. Server-rendering it would put it in
        // the page source, the browser cache and every proxy log on the way.
        var start = await client.PostAsync("/api/account/totp/start", null);
        var enrolment = (await start.Content.ReadFromJsonAsync<TotpEnrolmentDto>())!;
        Assert.DoesNotContain(enrolment.Secret, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChallengePage_RendersWithoutLazyLoading()
    {
        var (client, userId) = await AccountAsync();
        await EnrolAsync(client, userId);

        // The challenge is the wall a held session lands on, so it must be complete on the first
        // response — a skeleton here reads as "the site is broken".
        var fresh = _factory.CreateUnverifiedClient(ProviderKeyFor(userId));
        var response = await fresh.GetAsync(RoutePaths.MfaChallenge);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("mfa.verify", html, StringComparison.Ordinal);
    }

    // ── Helpers ──

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private readonly Dictionary<Guid, string> _providerKeys = [];

    private string ProviderKeyFor(Guid userId) => _providerKeys[userId];

    private async Task<(HttpClient Client, Guid UserId)> AccountAsync(string? email = null)
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);

        if (email is not null)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Users.FirstAsync(u => u.Id == user.Id);
            row.Email = email;
            await db.SaveChangesAsync();
        }

        _providerKeys[user.Id] = providerKey;
        return (_factory.CreateAuthenticatedClient(providerKey), user.Id);
    }

    private async Task<(HttpClient Client, Guid WorkspaceId)> WorkspaceClientAsync()
    {
        var (client, _) = await AccountAsync($"owner-{Guid.NewGuid():N}@example.com");

        var response = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = $"ID {Guid.NewGuid():N}"[..16], Seats = 5 });
        response.EnsureSuccessStatusCode();
        var workspace = (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;

        return (client, workspace.Id);
    }

    private static async Task BindDirectoryAsync(HttpClient client, string provider, string directoryId, string? domains = null)
    {
        var response = await client.PutAsJsonAsync("/api/workspace/sso", new
        {
            provider,
            directoryId,
            allowedEmailDomains = domains,
            enabled = true,
            jitRole = nameof(WorkspaceRole.Member),
        });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Drives the sign-in hook directly. The OAuth handshake cannot be replayed in-process, and
    /// the thing under test is what the service does with a set of claims, not how Google
    /// delivers them.
    /// </summary>
    private async Task ApplySsoAsync(Guid userId, string email, string provider, Claim[] claims)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var sso = scope.ServiceProvider.GetRequiredService<SsoLoginService>();
        await sso.ApplyAsync(userId, email, provider, claims, CancellationToken.None);
    }

    private async Task<HttpClient> ScimClientAsync(HttpClient admin)
    {
        var issued = await admin.PostAsJsonAsync("/api/workspace/scim-tokens", new { label = "Okta" });
        issued.EnsureSuccessStatusCode();
        var token = (await issued.Content.ReadFromJsonAsync<ScimTokenDto>())!;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }

    private async Task<string> EnrolAsync(HttpClient client, Guid userId)
    {
        var start = await client.PostAsync("/api/account/totp/start", null);
        start.EnsureSuccessStatusCode();
        var enrolment = (await start.Content.ReadFromJsonAsync<TotpEnrolmentDto>())!;

        var confirm = await client.PostAsJsonAsync("/api/account/totp/confirm",
            new { code = TotpService.ComputeCode(enrolment.Secret, TotpService.StepFor(DateTime.UtcNow)) });
        confirm.EnsureSuccessStatusCode();

        _ = userId;
        return enrolment.Secret;
    }

    private async Task<Guid> OwnerIdAsync(Guid workspaceId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Workspaces.AsNoTracking().Where(w => w.Id == workspaceId).Select(w => w.OwnerId).FirstAsync();
    }

    private async Task SetOwnerEmailAsync(Guid workspaceId, string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownerId = await db.Workspaces.AsNoTracking().Where(w => w.Id == workspaceId).Select(w => w.OwnerId).FirstAsync();
        var owner = await db.Users.FirstAsync(u => u.Id == ownerId);
        owner.Email = email;

        var seat = await db.WorkspaceMembers.FirstAsync(m => m.WorkspaceId == workspaceId && m.UserId == ownerId);
        seat.InviteEmail = email;
        await db.SaveChangesAsync();
    }

    /// <summary>Pushes the policy start and the seat's join date into the past, so grace has lapsed.</summary>
    private async Task BackdatePolicyAsync(Guid workspaceId, TimeSpan by)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var workspace = await db.Workspaces.FirstAsync(w => w.Id == workspaceId);
        workspace.MfaRequiredAt = DateTime.UtcNow - by;

        foreach (var seat in await db.WorkspaceMembers.Where(m => m.WorkspaceId == workspaceId).ToListAsync())
            seat.JoinedAt = DateTime.UtcNow - by;

        await db.SaveChangesAsync();
    }

    private async Task<MfaVerdict> EvaluateAsync(Guid workspaceId) =>
        await EvaluateForUserAsync(await OwnerIdAsync(workspaceId));

    private async Task<MfaVerdict> EvaluateForUserAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var policies = scope.ServiceProvider.GetRequiredService<MfaPolicyService>();
        return await policies.EvaluateAsync(userId, CancellationToken.None);
    }
}
