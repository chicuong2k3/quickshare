using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Downloads.Shared;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Features.Workspaces.Branding;
using QuickShare.Features.Workspaces.Domains;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// White-label (#81) and the preview watermark (#155) — the agency batch.
///
/// The invariants worth holding are about *claims*. A hostname is somebody's property, so
/// claiming one must change nothing until DNS agrees; a verified hostname belongs to exactly one
/// workspace, so no other workspace's share may be reachable through it; and a watermark is a
/// promise about who can screenshot the work, so a plan that cannot keep the promise is refused
/// rather than quietly ignored.
/// </summary>
public sealed class WhiteLabelTests : IClassFixture<TestWebAppFactory>
{
    private const string FileName = "storyboard.png";
    private const string ContentType = "image/png";
    private const long FileSize = 2048;

    private readonly TestWebAppFactory _factory;

    public WhiteLabelTests(TestWebAppFactory factory) => _factory = factory;

    // ── Claiming a hostname (#81) ──

    [Fact]
    public async Task Domain_IsRefusedBelowStudio()
    {
        var (client, _) = await WorkspaceClientAsync(PlanTierLimits.Pro);

        var response = await client.PutAsJsonAsync("/api/workspace/domain",
            new DomainRequest { Host = "files.pro-agency.test" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Domain_IsStoredUnverifiedAndChangesNothingUntilDnsAgrees()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();

        var response = await client.PutAsJsonAsync("/api/workspace/domain",
            new DomainRequest { Host = "HTTPS://Files.Unpointed.Test" });
        response.EnsureSuccessStatusCode();

        var dto = (await response.Content.ReadFromJsonAsync<DomainDto>())!;
        // A pasted URL is a misunderstanding worth correcting, not refusing.
        Assert.Equal("files.unpointed.test", dto.Host);
        Assert.False(dto.Verified);

        // Nothing is served under an unverified claim — the link stays canonical.
        var link = await ShareLinkAsync(client);
        Assert.DoesNotContain("files.unpointed.test", link, StringComparison.Ordinal);
        Assert.Equal(workspaceId, await OwnerOfAsync("files.unpointed.test"));
    }

    [Fact]
    public async Task Domain_RefusesOurOwnHost()
    {
        var (client, _) = await WorkspaceClientAsync();

        // Handing one tenant a subdomain of the deployment would hand them everyone's pages.
        var response = await client.PutAsJsonAsync("/api/workspace/domain",
            new DomainRequest { Host = "tenant.app.quickshare.test" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Domain_RefusesSomethingThatIsNotAHostname()
    {
        var (client, _) = await WorkspaceClientAsync();

        foreach (var candidate in new[] { "localhost", "files.agency.test:8443", "files.agency.test/uploads", "-bad.agency.test" })
        {
            var response = await client.PutAsJsonAsync("/api/workspace/domain", new DomainRequest { Host = candidate });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Domain_CanOnlyBeClaimedByOneWorkspace()
    {
        const string Host = "files.contested.test";
        var (first, _) = await WorkspaceClientAsync();
        (await first.PutAsJsonAsync("/api/workspace/domain", new DomainRequest { Host = Host })).EnsureSuccessStatusCode();

        var (second, _) = await WorkspaceClientAsync();
        var response = await second.PutAsJsonAsync("/api/workspace/domain", new DomainRequest { Host = Host });

        // Otherwise claiming a hostname would be a way to take over somebody else's links.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── Verification (#81) ──

    [Fact]
    public async Task Domain_VerifiesOnceItPointsAtUs()
    {
        const string Host = "files.pointed.test";
        var (client, _) = await WorkspaceClientAsync();
        (await client.PutAsJsonAsync("/api/workspace/domain", new DomainRequest { Host = Host })).EnsureSuccessStatusCode();

        // The first attempt is the normal one: DNS has not caught up.
        var early = (await (await client.PostAsync("/api/workspace/domain/verify", null))
            .Content.ReadFromJsonAsync<DomainDto>())!;
        Assert.False(early.Verified);
        Assert.NotNull(early.LastError);

        Point(Host);

        var verified = (await (await client.PostAsync("/api/workspace/domain/verify", null))
            .Content.ReadFromJsonAsync<DomainDto>())!;
        Assert.True(verified.Verified);
        Assert.Null(verified.LastError);
    }

    [Fact]
    public async Task Domain_StopsBeingVerifiedWhenItStopsPointingAtUs()
    {
        const string Host = "files.moved.test";
        var (client, _) = await WorkspaceClientAsync();
        await VerifiedDomainAsync(client, Host);

        Unpoint(Host);
        var rechecked = (await (await client.PostAsync("/api/workspace/domain/verify", null))
            .Content.ReadFromJsonAsync<DomainDto>())!;

        // Staying verified would keep us minting links to an address that no longer reaches us.
        Assert.False(rechecked.Verified);
    }

    [Fact]
    public async Task Domain_LosesItsVerificationWhenTheHostChanges()
    {
        var (client, _) = await WorkspaceClientAsync();
        await VerifiedDomainAsync(client, "files.first.test");

        var renamed = (await (await client.PutAsJsonAsync("/api/workspace/domain",
            new DomainRequest { Host = "files.second.test" })).Content.ReadFromJsonAsync<DomainDto>())!;

        Assert.False(renamed.Verified);
        // A new claim gets a new reference, so an old one cannot vouch for it.
        Assert.NotEqual(string.Empty, renamed.VerificationToken);
    }

    // ── What a verified hostname changes (#81) ──

    [Fact]
    public async Task Domain_BecomesTheLinkTheSenderHandsOut()
    {
        const string Host = "files.studio-nam.test";
        var (client, _) = await WorkspaceClientAsync();
        await VerifiedDomainAsync(client, Host);

        var link = await ShareLinkAsync(client);

        // An agency that white-labels the page and then hands out our hostname has
        // white-labelled nothing.
        Assert.StartsWith($"https://{Host}/d/", link, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Domain_DoesNotServeAnotherWorkspacesDelivery()
    {
        const string Host = "files.exclusive.test";
        var (client, _) = await WorkspaceClientAsync();
        await VerifiedDomainAsync(client, Host);

        var mine = await ShareCodeAsync(client);
        var (other, _) = await WorkspaceClientAsync();
        var theirs = await ShareCodeAsync(other);

        var anonymous = _factory.CreateClient();

        var ours = await GetPageAsync(anonymous, mine, Host);
        Assert.Contains(FileName, ours, StringComparison.Ordinal);

        // A client of this agency must never see somebody else's file under the agency's own
        // hostname — and a 404 rather than a redirect, which would confirm the code exists.
        var foreign = await GetPageAsync(anonymous, theirs, Host);
        Assert.DoesNotContain(FileName, foreign, StringComparison.Ordinal);

        // The same share is still perfectly reachable on the canonical address.
        Assert.Contains(FileName, await GetPageAsync(anonymous, theirs, host: null), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Domain_RemovalFallsBackToTheCanonicalAddress()
    {
        const string Host = "files.temporary.test";
        var (client, workspaceId) = await WorkspaceClientAsync();
        await VerifiedDomainAsync(client, Host);

        var response = await client.DeleteAsync("/api/workspace/domain");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Null(await OwnerOfAsync(Host));
        // Links already in somebody's inbox keep working; new ones go back to ours.
        Assert.DoesNotContain(Host, await ShareLinkAsync(client), StringComparison.Ordinal);
        Assert.NotEqual(Guid.Empty, workspaceId);
    }

    // ── Preview watermark (#155) ──

    [Fact]
    public async Task Watermark_IsRefusedOnAFreePlanRatherThanSilentlyDropped()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Free);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await CommitAsync(client, watermark: true);

        // A sender told their preview carries their name, when it does not, has been told
        // something untrue about who can screenshot their work.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Watermark_MarksThePreviewWithTheWorkspacesOwnLine()
    {
        var (client, _) = await WorkspaceClientAsync();
        (await client.PutAsJsonAsync("/api/workspace/brand", new BrandRequest
        {
            Enabled = true,
            HeaderText = "Studio Nam",
        })).EnsureSuccessStatusCode();

        var shareCode = await ShareCodeAsync(client, watermark: true);
        var page = await _factory.CreateClient().GetStringAsync($"/d/{shareCode}");

        Assert.Contains("dl-media--watermark", page, StringComparison.Ordinal);
        Assert.Contains("--dl-watermark", page, StringComparison.Ordinal);
        Assert.Contains("Studio%20Nam", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Watermark_IsAbsentUnlessTheSenderAsksForIt()
    {
        var (client, _) = await WorkspaceClientAsync();
        var shareCode = await ShareCodeAsync(client);

        var page = await _factory.CreateClient().GetStringAsync($"/d/{shareCode}");

        Assert.DoesNotContain("dl-media--watermark", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Watermark_TextCannotEscapeTheDataUri()
    {
        var css = PreviewWatermark.ToCssUrl("Nam \"</text><script>alert(1)</script>");

        // Escaped twice over: XML first, then percent-encoding. Nothing can close the element.
        Assert.DoesNotContain("<script>", css, StringComparison.Ordinal);
        Assert.DoesNotContain("</text>", css, StringComparison.Ordinal);
        Assert.StartsWith("url(\"data:image/svg+xml,", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Watermark_TextPrefersTheBrandLineAndFallsBackToNothing()
    {
        Assert.Equal("Studio Nam", PreviewWatermark.TextFor("Studio Nam", "Nam Nguyen"));
        Assert.Equal("Nam Nguyen", PreviewWatermark.TextFor(" ", "Nam Nguyen"));
        // A watermark reading nothing in particular marks nobody and only degrades the picture.
        Assert.Null(PreviewWatermark.TextFor(null, null));
    }

    // ── Helpers ──

    private static void Point(string host)
    {
        lock (StubDomainVerifier.Pointed)
        {
            StubDomainVerifier.Pointed.Add(host);
        }
    }

    private static void Unpoint(string host)
    {
        lock (StubDomainVerifier.Pointed)
        {
            StubDomainVerifier.Pointed.Remove(host);
        }
    }

    private static async Task VerifiedDomainAsync(HttpClient client, string host)
    {
        (await client.PutAsJsonAsync("/api/workspace/domain", new DomainRequest { Host = host })).EnsureSuccessStatusCode();
        Point(host);
        var verified = (await (await client.PostAsync("/api/workspace/domain/verify", null))
            .Content.ReadFromJsonAsync<DomainDto>())!;
        Assert.True(verified.Verified);
    }

    private async Task<Guid?> OwnerOfAsync(string host)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.WorkspaceDomains.AsNoTracking()
            .Where(d => d.Host == host)
            .Select(d => (Guid?)d.WorkspaceId)
            .FirstOrDefaultAsync();
    }

    private async Task<(HttpClient Client, Guid WorkspaceId)> WorkspaceClientAsync(string plan = PlanTierLimits.Studio)
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, plan);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = $"Agency {Guid.NewGuid():N}"[..20], Seats = 3 });
        response.EnsureSuccessStatusCode();
        var workspace = (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;

        return (client, workspace.Id);
    }

    private static async Task<HttpResponseMessage> CommitAsync(HttpClient client, bool watermark = false)
    {
        var startResponse = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = FileName,
            FileSize = FileSize,
            ContentType = ContentType,
            PartNumbers = [1],
        });
        startResponse.EnsureSuccessStatusCode();
        var start = (await startResponse.Content.ReadFromJsonAsync<StartR2Response>())!;

        return await client.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
        {
            DeliveryId = start.DeliveryId,
            UploadId = start.UploadId,
            ObjectKey = start.ObjectKey,
            FileName = FileName,
            ContentType = ContentType,
            FileSize = FileSize,
            Parts = [new R2PartEtag(1, "etag-1")],
            ExpiryHours = 24,
            WatermarkEnabled = watermark,
        });
    }

    private static async Task<string> ShareLinkAsync(HttpClient client, bool watermark = false)
    {
        var response = await CommitAsync(client, watermark);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CommitR2Response>())!.ShareLink;
    }

    private static async Task<string> ShareCodeAsync(HttpClient client, bool watermark = false)
    {
        var response = await CommitAsync(client, watermark);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CommitR2Response>())!.ShareCode;
    }

    private static async Task<string> GetPageAsync(HttpClient client, string shareCode, string? host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/d/{shareCode}");
        if (host is not null)
            request.Headers.Host = host;

        var response = await client.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }
}
