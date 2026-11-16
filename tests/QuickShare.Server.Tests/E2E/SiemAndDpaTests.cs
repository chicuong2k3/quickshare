using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Users.Dpa;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Features.Workspaces.Streaming;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Jobs;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Audit-log streaming to a SIEM (#191) and the self-serve DPA (#184).
///
/// The invariants worth defending: a sink only accepts HTTPS and a known format; the secret is
/// write-only; the wire formats are the ones each SIEM actually parses; batches are signed so a
/// receiver can prove provenance; and a DPA signature is evidence, so it is versioned and
/// cannot be silently re-stamped.
/// </summary>
public sealed class SiemAndDpaTests : IClassFixture<TestWebAppFactory>
{
    private const string Endpoint = "https://siem.example/ingest";
    private const string Secret = "shared-secret";

    private readonly TestWebAppFactory _factory;

    public SiemAndDpaTests(TestWebAppFactory factory) => _factory = factory;

    // ── Sink configuration (#191) ──

    [Fact]
    public async Task Sink_RefusesPlaintextEndpoints()
    {
        var (client, _) = await WorkspaceClientAsync();

        var response = await client.PutAsJsonAsync("/api/workspace/streaming",
            new SiemSinkRequest { Endpoint = "http://siem.example/ingest", Format = SiemFormat.Json });

        // An audit stream over plaintext is an audit stream somebody else can read, which
        // inverts the point of shipping it to a SOC.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sink_RefusesAnUnknownFormat()
    {
        var (client, _) = await WorkspaceClientAsync();

        var response = await client.PutAsJsonAsync("/api/workspace/streaming",
            new SiemSinkRequest { Endpoint = Endpoint, Format = "syslog-over-carrier-pigeon" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sink_StartsAtNowSoFirstSaveDoesNotBackfillHistory()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();
        // Some audit history already exists: creating the workspace wrote an event.
        var before = DateTime.UtcNow;

        var response = await client.PutAsJsonAsync("/api/workspace/streaming",
            new SiemSinkRequest { Endpoint = Endpoint, Format = SiemFormat.Json, Secret = Secret });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sink = await db.SiemSinks.AsNoTracking().SingleAsync(s => s.WorkspaceId == workspaceId);

        // Replaying months of history into somebody's SIEM on first save is a bill they did
        // not agree to.
        Assert.NotNull(sink.LastShippedAt);
        Assert.True(sink.LastShippedAt >= before);
    }

    [Fact]
    public async Task Sink_SecretIsWriteOnlyAndBlankLeavesItAlone()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();

        await client.PutAsJsonAsync("/api/workspace/streaming",
            new SiemSinkRequest { Endpoint = Endpoint, Format = SiemFormat.Json, Secret = Secret });

        var read = await client.GetFromJsonAsync<SiemSinkDto>("/api/workspace/streaming");
        Assert.NotNull(read);
        Assert.True(read.HasSecret);

        // Saving again without a secret must not wipe the stored one — the field is never
        // populated back into the form, so a blank submit is the normal case.
        await client.PutAsJsonAsync("/api/workspace/streaming",
            new SiemSinkRequest { Endpoint = Endpoint, Format = SiemFormat.Cef, Secret = null });

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sink = await db.SiemSinks.AsNoTracking().SingleAsync(s => s.WorkspaceId == workspaceId);
        Assert.Equal(Secret, sink.Secret);
        Assert.Equal(SiemFormat.Cef, sink.Format);
    }

    [Fact]
    public async Task Sink_SavingClearsAParkedFailureCount()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();
        await client.PutAsJsonAsync("/api/workspace/streaming",
            new SiemSinkRequest { Endpoint = Endpoint, Format = SiemFormat.Json, Secret = Secret });

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sink = await db.SiemSinks.SingleAsync(s => s.WorkspaceId == workspaceId);
            sink.ConsecutiveFailures = SiemSink.MaxConsecutiveFailures;
            sink.LastError = "HTTP 500";
            await db.SaveChangesAsync();
        }

        var parked = await client.GetFromJsonAsync<SiemSinkDto>("/api/workspace/streaming");
        Assert.NotNull(parked);
        Assert.True(parked.IsParked);

        // Fixing the endpoint and pressing save is the obvious gesture; a separate "resume"
        // button would be ceremony.
        await client.PutAsJsonAsync("/api/workspace/streaming",
            new SiemSinkRequest { Endpoint = Endpoint, Format = SiemFormat.Json });

        var resumed = await client.GetFromJsonAsync<SiemSinkDto>("/api/workspace/streaming");
        Assert.NotNull(resumed);
        Assert.False(resumed.IsParked);
        Assert.Equal(0, resumed.ConsecutiveFailures);
        Assert.Null(resumed.LastError);
    }

    [Fact]
    public async Task Sink_IsAdminOnly()
    {
        var (ownerClient, workspaceId) = await WorkspaceClientAsync();
        await ownerClient.PutAsJsonAsync("/api/workspace/streaming",
            new SiemSinkRequest { Endpoint = Endpoint, Format = SiemFormat.Json });

        var memberClient = await MemberClientAsync(workspaceId);

        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync("/api/workspace/streaming")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.DeleteAsync("/api/workspace/streaming")).StatusCode);
    }

    // ── Wire formats (#191) ──

    [Fact]
    public void Format_CefEscapesTheDelimitersItDependsOn()
    {
        var events = new[]
        {
            new SiemEvent(
                Guid.Empty,
                new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc),
                "workspacememberjoined",
                Guid.Empty,
                "a@b.test",
                Guid.Empty,
                "hash",
                "Chrome",
                "{\"role\":\"member=admin\"}"),
        };

        var cef = SiemEventFormatter.Render(SiemFormat.Cef, events);

        Assert.StartsWith("CEF:0|QuickShare|", cef, StringComparison.Ordinal);
        // An unescaped '=' inside a value silently swallows the following field.
        Assert.Contains("member\\=admin", cef, StringComparison.Ordinal);
        Assert.EndsWith("\n", cef, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_SplunkEmitsNewlineDelimitedObjectsNotAnArray()
    {
        var events = new[] { Sample("a@b.test"), Sample("c@d.test") };

        var body = SiemEventFormatter.Render(SiemFormat.SplunkHec, events);

        // HEC rejects a JSON array — the kind of detail that only surfaces in production if it
        // is not encoded here.
        Assert.False(body.TrimStart().StartsWith('['));
        Assert.Equal(2, body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("\"sourcetype\":\"quickshare:audit\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_JsonWrapsEventsInAnEnvelope()
    {
        var body = SiemEventFormatter.Render(SiemFormat.Json, [Sample("a@b.test")]);

        Assert.StartsWith("{\"events\":[", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Signature_IsHmacOverTimestampAndBody()
    {
        const string Timestamp = "1755772800";
        const string Body = "{\"events\":[]}";

        var signature = SiemShippingJob.Sign(Secret, Timestamp, Body);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var expected = Convert.ToHexStringLower(
            hmac.ComputeHash(Encoding.UTF8.GetBytes($"{Timestamp}.{Body}")));

        // The timestamp is inside the signed payload so a captured batch cannot be replayed
        // later as though it were fresh.
        Assert.Equal(expected, signature);
    }

    [Fact]
    public void Filter_DropsUnparseableNamesInsteadOfFailingWholesale()
    {
        var parsed = SiemShippingJob.ParseFilter("WorkspaceMemberJoined, not-a-real-event, DpaSigned");

        Assert.Equal(2, parsed.Count);
        Assert.Contains(SecurityEventType.WorkspaceMemberJoined, parsed);
        Assert.Contains(SecurityEventType.DpaSigned, parsed);
        Assert.Empty(SiemShippingJob.ParseFilter("   "));
    }

    // ── DPA (#184) ──

    [Fact]
    public async Task Dpa_StartsUnsignedAndReportsTheCurrentVersion()
    {
        var (client, _) = await AccountClientAsync();

        var status = await client.GetFromJsonAsync<DpaStatusDto>("/api/dpa");

        Assert.NotNull(status);
        Assert.False(status.IsSigned);
        Assert.False(status.NeedsResign);
        Assert.Equal(DpaTerms.CurrentVersion, status.CurrentVersion);
    }

    [Fact]
    public async Task Dpa_SigningRecordsTheEvidenceAndAuditsIt()
    {
        var (client, userId) = await AccountClientAsync();

        var response = await client.PostAsJsonAsync("/api/dpa/sign",
            new SignDpaRequest { CompanyName = "Acme BV", SignerName = "R. Vos, COO" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var status = await response.Content.ReadFromJsonAsync<DpaStatusDto>();
        Assert.NotNull(status);
        Assert.True(status.IsSigned);
        Assert.Equal("Acme BV", status.CompanyName);
        // The signer email falls back to the account email — the account is already the
        // verified identity, so demanding it again is friction without a purpose.
        Assert.Equal(TestWebAppFactory.TestUserEmail, status.SignerEmail);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var signature = await db.DpaSignatures.AsNoTracking().SingleAsync(d => d.UserId == userId);
        Assert.Equal(DpaTerms.CurrentVersion, signature.Version);
        // The IP is hashed, like every other address this system keeps.
        Assert.NotEmpty(signature.IpHash);

        Assert.Contains(await db.SecurityEvents.AsNoTracking().ToListAsync(),
            e => e.UserId == userId && e.EventType == SecurityEventType.DpaSigned);
    }

    [Fact]
    public async Task Dpa_RequiresACompanyAndASigner()
    {
        var (client, _) = await AccountClientAsync();

        var response = await client.PostAsJsonAsync("/api/dpa/sign",
            new SignDpaRequest { CompanyName = "   ", SignerName = "R. Vos" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Dpa_SigningTheSameVersionTwiceIsRefused()
    {
        var (client, userId) = await AccountClientAsync();

        await client.PostAsJsonAsync("/api/dpa/sign",
            new SignDpaRequest { CompanyName = "Acme BV", SignerName = "R. Vos" });

        var again = await client.PostAsJsonAsync("/api/dpa/sign",
            new SignDpaRequest { CompanyName = "Acme BV", SignerName = "Someone Else" });

        // A second row would make "when did they agree" ambiguous, which is the one question
        // the record exists to answer.
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.DpaSignatures.AsNoTracking().CountAsync(d => d.UserId == userId));
    }

    [Fact]
    public async Task Dpa_ASignatureAgainstAnOlderVersionAsksForARe_sign()
    {
        var (client, userId) = await AccountClientAsync();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.DpaSignatures.Add(new DpaSignature
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                Version = "0.9",
                CompanyName = "Acme BV",
                SignerName = "R. Vos",
                SignerEmail = TestWebAppFactory.TestUserEmail,
                SignedAt = DateTime.UtcNow.AddYears(-1),
                CreatedAt = DateTime.UtcNow.AddYears(-1),
            });
            await db.SaveChangesAsync();
        }

        var status = await client.GetFromJsonAsync<DpaStatusDto>("/api/dpa");

        Assert.NotNull(status);
        Assert.True(status.IsSigned);
        // Still a real agreement — just not agreement to the terms published today.
        Assert.True(status.NeedsResign);
        Assert.Equal("0.9", status.SignedVersion);
    }

    // ── Helpers ──

    private static SiemEvent Sample(string email) => new(
        Guid.NewGuid(),
        DateTime.UtcNow,
        "workspacecreated",
        Guid.NewGuid(),
        email,
        Guid.NewGuid(),
        "hash",
        "Chrome",
        null);

    private async Task<(HttpClient Client, Guid UserId)> AccountClientAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        return (_factory.CreateAuthenticatedClient(providerKey), user.Id);
    }

    private async Task<(HttpClient Client, Guid WorkspaceId)> WorkspaceClientAsync()
    {
        var (client, _) = await AccountClientAsync();
        var response = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = $"SIEM {Guid.NewGuid():N}"[..20], Seats = 3 });
        response.EnsureSuccessStatusCode();
        var workspace = (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;
        return (client, workspace.Id);
    }

    /// <summary>A plain member of an existing workspace — seeded straight in, then accepted.</summary>
    private async Task<HttpClient> MemberClientAsync(Guid workspaceId)
    {
        var token = Guid.NewGuid().ToString("N");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId,
                InviteEmail = $"member-{token[..8]}@example.com",
                Role = WorkspaceRole.Member,
                InviteTokenHash = QuickShare.Features.Workspaces.Shared.WorkspaceService.HashToken(token),
                InviteExpiresAt = DateTime.UtcNow.AddDays(WorkspaceMember.InviteValidDays),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var (client, _) = await AccountClientAsync();
        var accept = await client.PostAsJsonAsync("/api/workspace/invites/accept",
            new AcceptInviteRequest { Token = token });
        accept.EnsureSuccessStatusCode();
        return client;
    }
}
