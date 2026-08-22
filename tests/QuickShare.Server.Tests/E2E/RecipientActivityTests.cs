using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Deliveries.Recipients;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.AddFile;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Per-recipient tracking (#37) and per-file counts (#154).
///
/// The multi-recipient half of #37 shipped years of batches ago; what was missing is the half
/// its acceptance actually names — *who* opened it and *who* downloaded it. A sender who mails
/// five people and reads "3 downloads" still does not know which two to chase.
///
/// The rules worth holding: a token attributes only its own delivery, privacy mode records
/// nothing at all, and a recipient list is the sender's to see and nobody else's.
/// </summary>
public sealed class RecipientActivityTests : IClassFixture<TestWebAppFactory>
{
    private const string FileName = "pitch.pdf";
    private const string ContentType = "application/pdf";
    private const long FileSize = 1024;
    private const string First = "one@example.test";
    private const string Second = "two@example.test";

    private readonly TestWebAppFactory _factory;

    public RecipientActivityTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Commit_RecordsOneRowPerAddressedRecipient()
    {
        var (client, _) = await SenderAsync();
        var deliveryId = await UploadAsync(client, [First, Second]);

        var recipients = await RecipientsAsync(deliveryId);

        Assert.Equal(2, recipients.Count);
        // Each gets their own token, or a forwarded mail would be indistinguishable from the
        // one it was forwarded from.
        Assert.Equal(2, recipients.Select(r => r.Token).Distinct().Count());
        Assert.All(recipients, r => Assert.Null(r.OpenedAt));
    }

    [Fact]
    public async Task OpeningWithATokenMarksThatRecipientOnly()
    {
        var (client, _) = await SenderAsync();
        var deliveryId = await UploadAsync(client, [First, Second]);
        var shareCode = await ShareCodeAsync(deliveryId);
        var recipients = await RecipientsAsync(deliveryId);
        var mine = recipients.Single(r => r.Email == First);

        await _factory.CreateClient().GetAsync($"/d/{shareCode}?{DeliveryRecipient.TokenQueryKey}={mine.Token}");

        var after = await RecipientsAsync(deliveryId);
        Assert.NotNull(after.Single(r => r.Email == First).OpenedAt);
        Assert.Null(after.Single(r => r.Email == Second).OpenedAt);
    }

    [Fact]
    public async Task DownloadingWithATokenMarksTheDownload()
    {
        var (client, _) = await SenderAsync();
        var deliveryId = await UploadAsync(client, [First]);
        var shareCode = await ShareCodeAsync(deliveryId);
        var token = (await RecipientsAsync(deliveryId)).Single().Token;

        // Not following the redirect: it points at storage, and the test client would chase it
        // back into the app with the same headers and read the wrong status.
        using var direct = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await direct.GetAsync($"/api/downloads/{shareCode}?{DeliveryRecipient.TokenQueryKey}={token}");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var recipient = await EventuallyAsync(deliveryId, r => r.DownloadedAt is not null);

        // A download implies an open even when the link was clicked straight out of a mail
        // client that never rendered the page.
        Assert.NotNull(recipient.DownloadedAt);
        Assert.NotNull(recipient.OpenedAt);
    }

    [Fact]
    public async Task ATokenFromAnotherDeliveryMarksNothing()
    {
        var (client, _) = await SenderAsync();
        var mine = await UploadAsync(client, [First]);
        var other = await UploadAsync(client, [Second]);

        var strangerToken = (await RecipientsAsync(other)).Single().Token;
        var shareCode = await ShareCodeAsync(mine);

        await _factory.CreateClient().GetAsync($"/d/{shareCode}?{DeliveryRecipient.TokenQueryKey}={strangerToken}");

        // Scoped by delivery as well as by token: a token from one share must never resolve
        // against another.
        Assert.All(await RecipientsAsync(other), r => Assert.Null(r.OpenedAt));
        Assert.All(await RecipientsAsync(mine), r => Assert.Null(r.OpenedAt));
    }

    [Fact]
    public async Task PrivacyMode_RecordsNoRecipientsAtAll()
    {
        var (client, _) = await SenderAsync();
        var deliveryId = await UploadAsync(client, [First, Second], privacyMode: true);

        // A share that deliberately keeps no download record must not keep a per-person one
        // instead — the recipients still get their mail, nothing is written down.
        Assert.Empty(await RecipientsAsync(deliveryId));

        var activity = await ActivityAsync(client, deliveryId);
        Assert.True(activity.PrivacyMode);
        Assert.Empty(activity.Recipients);
    }

    [Fact]
    public async Task Activity_IsTheSendersAndNobodyElses()
    {
        var (client, _) = await SenderAsync();
        var deliveryId = await UploadAsync(client, [First]);

        var (stranger, _) = await SenderAsync();
        var response = await stranger.GetAsync($"/api/deliveries/{deliveryId}/activity");

        // A recipient list is a list of people somebody chose to mail.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Activity_ShowsWhoOpenedAndWhoDownloaded()
    {
        var (client, _) = await SenderAsync();
        var deliveryId = await UploadAsync(client, [First, Second]);
        var shareCode = await ShareCodeAsync(deliveryId);
        var token = (await RecipientsAsync(deliveryId)).Single(r => r.Email == First).Token;

        await _factory.CreateClient().GetAsync($"/d/{shareCode}?{DeliveryRecipient.TokenQueryKey}={token}");

        var activity = await ActivityAsync(client, deliveryId);

        Assert.Equal(2, activity.Recipients.Count);
        Assert.NotNull(activity.Recipients.Single(r => r.Email == First).OpenedAt);
        Assert.Null(activity.Recipients.Single(r => r.Email == Second).OpenedAt);
    }

    [Fact]
    public async Task PerFileDownloads_AreCountedSeparatelyFromTheShare()
    {
        var (client, _) = await SenderAsync();
        var deliveryId = await UploadAsync(client, [], extraFile: true);
        var shareCode = await ShareCodeAsync(deliveryId);

        Guid fileId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            fileId = await db.DeliveryFiles.AsNoTracking()
                .Where(f => f.DeliveryId == deliveryId)
                .OrderBy(f => f.SortOrder)
                .Select(f => f.Id)
                .FirstAsync();
        }

        await _factory.CreateClient().GetAsync($"/api/downloads/{shareCode}/files/{fileId}");

        var activity = await ActivityAsync(client, deliveryId);
        var file = activity.Files.Single(f => f.DownloadCount > 0);

        // Taking one file out of a bundle is not the same event as taking the bundle.
        Assert.Equal(1, file.DownloadCount);
    }

    // ── Helpers ──

    private async Task<(HttpClient Client, Guid UserId)> SenderAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);

        return (_factory.CreateAuthenticatedClient(providerKey), user.Id);
    }

    private static async Task<Guid> UploadAsync(
        HttpClient client, string[] recipients, bool privacyMode = false, bool extraFile = false)
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

        List<CommitFileRequest>? additional = null;
        if (extraFile)
        {
            var addResponse = await client.PostAsJsonAsync($"/api/uploads/{start.DeliveryId}/files", new
            {
                fileName = "appendix.pdf",
                fileSize = FileSize,
                contentType = ContentType,
                partNumbers = new[] { 1 },
            });
            addResponse.EnsureSuccessStatusCode();
            var added = await addResponse.Content.ReadFromJsonAsync<AddFileResponse>();

            additional =
            [
                new CommitFileRequest
                {
                    ObjectKey = added!.ObjectKey,
                    UploadId = added.UploadId,
                    FileName = "appendix.pdf",
                    ContentType = ContentType,
                    FileSize = FileSize,
                    Parts = [new R2PartEtag(1, "etag-1")],
                },
            ];
        }

        var commit = await client.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
        {
            DeliveryId = start.DeliveryId,
            UploadId = start.UploadId,
            ObjectKey = start.ObjectKey,
            FileName = FileName,
            ContentType = ContentType,
            FileSize = FileSize,
            Parts = [new R2PartEtag(1, "etag-1")],
            ExpiryHours = 24,
            RecipientEmails = [.. recipients],
            PrivacyMode = privacyMode,
            AdditionalFiles = additional,
        });
        commit.EnsureSuccessStatusCode();

        return start.DeliveryId;
    }

    private async Task<List<DeliveryRecipient>> RecipientsAsync(Guid deliveryId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.DeliveryRecipients.AsNoTracking()
            .Where(r => r.DeliveryId == deliveryId)
            .ToListAsync();
    }

    /// <summary>The download counter is stamped off the request thread; wait for it briefly.</summary>
    private async Task<DeliveryRecipient> EventuallyAsync(Guid deliveryId, Func<DeliveryRecipient, bool> until)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var recipient = (await RecipientsAsync(deliveryId)).Single();
            if (until(recipient))
                return recipient;

            await Task.Delay(100);
        }

        return (await RecipientsAsync(deliveryId)).Single();
    }

    private static async Task<DeliveryActivityDto> ActivityAsync(HttpClient client, Guid deliveryId)
    {
        var response = await client.GetAsync($"/api/deliveries/{deliveryId}/activity");
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<DeliveryActivityDto>())!;
    }

    private async Task<string> ShareCodeAsync(Guid deliveryId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Deliveries.AsNoTracking()
            .Where(d => d.Id == deliveryId)
            .Select(d => d.ShareCode)
            .SingleAsync();
    }
}
