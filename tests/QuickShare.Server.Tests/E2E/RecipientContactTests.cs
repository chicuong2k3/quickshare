using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Features.Users.Recipients;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Recent recipients + address book (issue #316): recipient emails are captured
/// at commit time for signed-in users (never in privacy mode, never anonymous),
/// served back via /api/me/recipients as quick-pick chips, and carried in the
/// GDPR export.
/// </summary>
public sealed class RecipientContactTests : IClassFixture<TestWebAppFactory>
{
    private const string TestFileName = "recipients.pdf";
    private const string TestContentType = "application/pdf";
    private const long TestFileSize = 1024;

    private readonly TestWebAppFactory _factory;

    public RecipientContactTests(TestWebAppFactory factory) => _factory = factory;

    /// <summary>
    /// A fresh account per case. These tests assert absolute contact counts, and the in-memory
    /// database is shared across the whole run — on the default user they were order-dependent
    /// and permanently red, which is worse than no test: a run that is always red is a run
    /// nobody reads.
    /// </summary>
    private async Task<(HttpClient Client, Guid UserId)> IsolatedClientAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        return (_factory.CreateAuthenticatedClient(providerKey), user.Id);
    }

    [Fact]
    public async Task Commit_WithRecipients_CapturesContacts()
    {
        var (client, userId) = await IsolatedClientAsync();
        await UploadFileAsync(client, recipients: ["alice@example.com", "bob@example.com"]);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contacts = await db.RecipientContacts
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.Email)
                .ToListAsync();
            Assert.Equal(2, contacts.Count);
            Assert.Equal("alice@example.com", contacts[0].Email);
            Assert.Equal("bob@example.com", contacts[1].Email);
            Assert.Equal(1, contacts[0].UseCount);
            Assert.NotEqual(DateTime.MinValue, contacts[0].LastUsedAt);
        }
    }

    [Fact]
    public async Task Commit_RepeatedRecipient_BumpsUseCount()
    {
        var (client, userId) = await IsolatedClientAsync();
        await UploadFileAsync(client, recipients: ["alice@example.com"]);
        await UploadFileAsync(client, recipients: ["alice@example.com"]);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contact = await db.RecipientContacts
                .SingleAsync(c => c.UserId == userId && c.Email == "alice@example.com");
            Assert.Equal(2, contact.UseCount);
        }
    }

    [Fact]
    public async Task Commit_PrivacyMode_DoesNotCapture()
    {
        var (client, userId) = await IsolatedClientAsync();
        await UploadFileAsync(client, recipients: ["alice@example.com"], privacyMode: true);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var count = await db.RecipientContacts.CountAsync(c => c.UserId == userId);
            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task Commit_Anonymous_NotCaptured()
    {
        int countBefore;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            countBefore = await db.RecipientContacts.CountAsync();
        }

        var client = _factory.CreateClient();
        await UploadFileAsync(client, recipients: ["anon@example.com"]);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var countAfter = await db.RecipientContacts.CountAsync();
            Assert.Equal(countBefore, countAfter);
            Assert.DoesNotContain(await db.RecipientContacts.ToListAsync(), c => c.Email == "anon@example.com");
        }
    }

    [Fact]
    public async Task List_ReturnsRecentFirst()
    {
        var (client, listUserId) = await IsolatedClientAsync();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RecipientContacts.RemoveRange(db.RecipientContacts.Where(c => c.UserId == listUserId));
            db.RecipientContacts.AddRange(
                new RecipientContact
                {
                    Id = Guid.CreateVersion7(),
                    UserId = listUserId,
                    Email = "old@example.com",
                    LastUsedAt = DateTime.UtcNow.AddDays(-2),
                    UseCount = 1,
                },
                new RecipientContact
                {
                    Id = Guid.CreateVersion7(),
                    UserId = listUserId,
                    Email = "new@example.com",
                    LastUsedAt = DateTime.UtcNow,
                    UseCount = 3,
                });
            await db.SaveChangesAsync();
        }

        var contacts = await client.GetFromJsonAsync<List<RecipientDto>>("/api/me/recipients");
        Assert.NotNull(contacts);
        Assert.Equal(2, contacts.Count);
        Assert.Equal("new@example.com", contacts[0].Email);
        Assert.Equal("old@example.com", contacts[1].Email);
        Assert.Equal(3, contacts[0].UseCount);
    }

    [Fact]
    public async Task List_IsScopedToOwnUser()
    {
        var otherId = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(otherId);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RecipientContacts.Add(new RecipientContact
            {
                Id = Guid.CreateVersion7(),
                UserId = Guid.Parse(otherId),
                Email = "other@example.com",
                LastUsedAt = DateTime.UtcNow,
                UseCount = 1,
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateAuthenticatedClient();
        var contacts = await client.GetFromJsonAsync<List<RecipientDto>>("/api/me/recipients");
        Assert.NotNull(contacts);
        Assert.DoesNotContain(contacts, c => c.Email == "other@example.com");
    }

    [Fact]
    public async Task Save_UpsertsByName()
    {
        var client = _factory.CreateAuthenticatedClient();
        var first = await client.PostAsJsonAsync("/api/me/recipients", new SaveRecipientRequest { Email = "Alice@Example.com", Name = "Alice" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var created = await first.Content.ReadFromJsonAsync<RecipientDto>();
        Assert.NotNull(created);
        Assert.Equal("alice@example.com", created.Email);
        Assert.Equal("Alice", created.Name);

        var second = await client.PostAsJsonAsync("/api/me/recipients", new SaveRecipientRequest { Email = "alice@example.com", Name = "Alice A." });
        var updated = await second.Content.ReadFromJsonAsync<RecipientDto>();
        Assert.NotNull(updated);
        Assert.Equal("Alice A.", updated.Name);
    }

    [Fact]
    public async Task Save_InvalidEmail_Rejected()
    {
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/me/recipients", new SaveRecipientRequest { Email = "not-an-email" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesOwnContact()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RecipientContacts.RemoveRange(db.RecipientContacts.Where(c => c.UserId == _factory.TestUserId));
            db.RecipientContacts.Add(new RecipientContact
            {
                Id = Guid.CreateVersion7(),
                UserId = _factory.TestUserId,
                Email = "alice@example.com",
                LastUsedAt = DateTime.UtcNow,
                UseCount = 1,
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateAuthenticatedClient();
        var contacts = await client.GetFromJsonAsync<List<RecipientDto>>("/api/me/recipients");
        var contact = Assert.Single(contacts!);

        var response = await client.DeleteAsync($"/api/me/recipients/{contact.Id}");
        Assert.True(response.IsSuccessStatusCode);

        await using (var verify = _factory.Services.CreateAsyncScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await db.RecipientContacts.CountAsync(c => c.UserId == _factory.TestUserId));
        }
    }

    [Fact]
    public async Task Delete_OthersContact_NotFound()
    {
        var otherId = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(otherId);
        Guid otherContactId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contact = new RecipientContact
            {
                Id = Guid.CreateVersion7(),
                UserId = Guid.Parse(otherId),
                Email = "other@example.com",
                LastUsedAt = DateTime.UtcNow,
                UseCount = 1,
            };
            db.RecipientContacts.Add(contact);
            await db.SaveChangesAsync();
            otherContactId = contact.Id;
        }

        var client = _factory.CreateAuthenticatedClient();
        var response = await client.DeleteAsync($"/api/me/recipients/{otherContactId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Export_IncludesRecipientContacts()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RecipientContacts.RemoveRange(db.RecipientContacts.Where(c => c.UserId == _factory.TestUserId));
            db.RecipientContacts.Add(new RecipientContact
            {
                Id = Guid.CreateVersion7(),
                UserId = _factory.TestUserId,
                Email = "alice@example.com",
                Name = "Alice",
                LastUsedAt = DateTime.UtcNow,
                UseCount = 5,
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateAuthenticatedClient();
        var export = await client.GetFromJsonAsync<ExportPayload>("/api/me/export");
        Assert.NotNull(export);
        var contact = Assert.Single(export.RecipientContacts);
        Assert.Equal("alice@example.com", contact.Email);
        Assert.Equal("Alice", contact.Name);
        Assert.Equal(5, contact.UseCount);
    }

    private async Task<CommitR2Response> UploadFileAsync(HttpClient client, List<string>? recipients = null, bool privacyMode = false)
    {
        var startResponse = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = TestFileName,
            FileSize = TestFileSize,
            ContentType = TestContentType,
            PartNumbers = [1],
        });
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var start = await startResponse.Content.ReadFromJsonAsync<StartR2Response>();
        Assert.NotNull(start);

        var commitResponse = await client.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
        {
            DeliveryId = start.DeliveryId,
            UploadId = start.UploadId,
            ObjectKey = start.ObjectKey,
            FileName = TestFileName,
            ContentType = TestContentType,
            FileSize = TestFileSize,
            Parts = [new R2PartEtag(1, "etag-1")],
            RecipientEmails = recipients,
            PrivacyMode = privacyMode,
        });
        Assert.Equal(HttpStatusCode.Created, commitResponse.StatusCode);
        var commit = await commitResponse.Content.ReadFromJsonAsync<CommitR2Response>();
        Assert.NotNull(commit);
        return commit;
    }

    private sealed class RecipientDto
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? Name { get; set; }
        public DateTime LastUsedAt { get; set; }
        public int UseCount { get; set; }
    }

    private sealed class ExportPayload
    {
        public List<ExportRecipientContact> RecipientContacts { get; set; } = [];
    }

    private sealed class ExportRecipientContact
    {
        public string Email { get; set; } = string.Empty;
        public string? Name { get; set; }
        public int UseCount { get; set; }
    }
}