using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.FileRequests.Create;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// The anonymous submission page (#16's blocked half) and drop rooms (#17).
///
/// The owner side of file requests shipped batches ago; the page a stranger actually uses never
/// existed, because the encryption question was left open. It is answered here: a drop cannot be
/// end-to-end encrypted, because E2E keeps the key in the URL fragment and there is nobody to
/// hand it to — the owner has to be able to open what was dropped. The page says so out loud.
///
/// The rule that separates the two shapes: a **room** shows everybody what everybody dropped, a
/// **request** shows nobody anything. One submitter must not see another's files just because
/// they both had the link.
/// </summary>
public sealed class DropRoomTests : IClassFixture<TestWebAppFactory>
{
    private const string FileName = "photo.jpg";
    private const string ContentType = "image/jpeg";
    private const long FileSize = 2048;

    private readonly TestWebAppFactory _factory;

    public DropRoomTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Page_ShowsTheRequestAndSaysWhatItCannotPromise()
    {
        var (client, _) = await OwnerAsync();
        var code = await CreateAsync(client, "Wedding photos");

        var html = await _factory.CreateClient().GetStringAsync($"/r/{code}");

        Assert.Contains("Wedding photos", html, StringComparison.Ordinal);
        Assert.Contains("request.upload", html, StringComparison.Ordinal);
        // The honest note this page exists to make.
        Assert.Contains("not end-to-end encrypted", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Page_ForAnUnknownCodeSaysNothingAboutWhetherItExisted()
    {
        var html = await _factory.CreateClient().GetStringAsync("/r/does-not-exist");

        // One generic state for expired, closed and missing: the page must not become an oracle
        // for which codes exist.
        Assert.Contains("not available", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("request.upload", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stranger_CanSubmitWithoutAnAccount()
    {
        var (client, owner) = await OwnerAsync();
        var code = await CreateAsync(client, "Send me the invoice");

        var deliveryId = await SubmitAsync(code);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId);

        // The submission belongs to the request, not to the submitter — there is no account to
        // own it — and it is not charged to a stranger's quota either.
        Assert.NotNull(delivery.FileRequestId);
        Assert.Null(delivery.SenderId);
        Assert.NotEqual(owner.Id, delivery.SenderId);
    }

    [Fact]
    public async Task Room_ShowsEverybodyWhatEverybodyDropped()
    {
        var (client, _) = await OwnerAsync();
        var code = await CreateAsync(client, "Party photos", FileRequestKind.Room);

        await SubmitAsync(code);

        var html = await _factory.CreateClient().GetStringAsync($"/r/{code}");

        // That is what makes it a room rather than a request.
        Assert.Contains(FileName, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_ShowsNobodyElsesSubmissions()
    {
        var (client, _) = await OwnerAsync();
        var code = await CreateAsync(client, "Private request");

        await SubmitAsync(code);

        var html = await _factory.CreateClient().GetStringAsync($"/r/{code}");

        // One submitter must not see another's files just because they both had the link.
        Assert.DoesNotContain(FileName, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClosedRequest_TakesNothingMore()
    {
        var (client, _) = await OwnerAsync();
        var code = await CreateAsync(client, "Closing time");

        // The close endpoint takes the request's id, not its public code.
        Guid requestId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            requestId = await db.FileRequests.AsNoTracking().Where(r => r.Code == code).Select(r => r.Id).SingleAsync();
        }

        var close = await client.PostAsync($"/api/file-requests/{requestId}/close", null);
        close.EnsureSuccessStatusCode();

        var start = await _factory.CreateClient().PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = FileName,
            FileSize = FileSize,
            ContentType = ContentType,
            PartNumbers = [1],
            RequestCode = code,
        });

        Assert.NotEqual(HttpStatusCode.OK, start.StatusCode);
    }

    [Fact]
    public async Task Kind_DefaultsToThePrivateShape()
    {
        var (client, _) = await OwnerAsync();

        var response = await client.PostAsJsonAsync("/api/file-requests",
            new CreateFileRequestRequest { Title = "Unspecified", Kind = "something-else" });
        response.EnsureSuccessStatusCode();

        // Anything unrecognised is a request: the private shape is the safe default.
        var created = (await response.Content.ReadFromJsonAsync<CreateFileRequestResponse>())!;
        Assert.Equal(FileRequestKind.Request, created.Kind);
    }

    [Fact]
    public async Task RequestsPage_CanActuallyCreateOne()
    {
        var (client, _) = await OwnerAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/account/requests");
        var html = await (await client.SendAsync(request)).Content.ReadAsStringAsync();

        // The create endpoints had existed since the file-request batch with nothing calling
        // them — the same API-only defect the batch-11 audit found in four other screens.
        Assert.Contains("request.create", html, StringComparison.Ordinal);
        Assert.Contains(FileRequestKind.Room, html, StringComparison.Ordinal);
    }

    // ── Helpers ──

    private async Task<(HttpClient Client, User Owner)> OwnerAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);

        return (_factory.CreateAuthenticatedClient(providerKey), user);
    }

    private static async Task<string> CreateAsync(HttpClient client, string title, string? kind = null)
    {
        var response = await client.PostAsJsonAsync("/api/file-requests",
            new CreateFileRequestRequest { Title = title, Kind = kind });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreateFileRequestResponse>())!.Code;
    }

    /// <summary>An anonymous drop: no cookie, no key, just the request code.</summary>
    private async Task<Guid> SubmitAsync(string code)
    {
        var anonymous = _factory.CreateClient();

        var startResponse = await anonymous.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = FileName,
            FileSize = FileSize,
            ContentType = ContentType,
            PartNumbers = [1],
            RequestCode = code,
        });
        startResponse.EnsureSuccessStatusCode();
        var start = (await startResponse.Content.ReadFromJsonAsync<StartR2Response>())!;

        var commit = await anonymous.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
        {
            DeliveryId = start.DeliveryId,
            UploadId = start.UploadId,
            ObjectKey = start.ObjectKey,
            FileName = FileName,
            ContentType = ContentType,
            FileSize = FileSize,
            Parts = [new R2PartEtag(1, "etag-1")],
        });
        commit.EnsureSuccessStatusCode();

        return start.DeliveryId;
    }
}
