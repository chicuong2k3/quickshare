using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.FileRequests.List;
using QuickShare.Infrastructure.Constants;
using QuickShare.Querying;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// The /account/requests tab (issue #16): the owner creates a request, lists their
/// own requests newest-first, and soft-closes a request so it stops accepting files.
/// </summary>
public sealed class FileRequestsApiTests : IClassFixture<TestWebAppFactory>
{
    private const string FileRequestsApi = "/api/file-requests";

    private readonly TestWebAppFactory _factory;

    public FileRequestsApiTests(TestWebAppFactory factory) => _factory = factory;

    private sealed record RequestRow(
        Guid Id, string Code, string Title, DateTime ExpiresAt, DateTime? ClosedAt,
        DateTime CreatedAt, long MaxTotalBytes, int MaxFiles, long CurrentTotalBytes, int CurrentFileCount);

    private async Task<string> CreateRequestAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync(FileRequestsApi, new { Title = title });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreateRequestResponse>();
        Assert.NotNull(created);
        Assert.False(string.IsNullOrEmpty(created.Code));
        return created.Code;
    }

    private sealed record CreateRequestResponse(string Code, string Title, DateTime ExpiresAt);

    [Fact]
    public async Task CreateAndList_ReturnsOwnRequest()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(providerKey);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var code = await CreateRequestAsync(client, "Wedding photos");

        var page = await client.GetFromJsonAsync<CursorPage<RequestRow>>(FileRequestsApi);

        Assert.NotNull(page);
        var row = Assert.Single(page.Items);
        Assert.Equal(code, row.Code);
        Assert.Equal("Wedding photos", row.Title);
        Assert.Null(row.ClosedAt);
    }

    [Fact]
    public async Task List_OnlyReturnsCallersOwnRequests()
    {
        var mineKey = Guid.CreateVersion7().ToString();
        var theirsKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(mineKey);
        await _factory.SeedUserAsync(theirsKey);

        var mine = _factory.CreateAuthenticatedClient(mineKey);
        var theirs = _factory.CreateAuthenticatedClient(theirsKey);

        await CreateRequestAsync(mine, "mine-a");
        await CreateRequestAsync(mine, "mine-b");
        await CreateRequestAsync(theirs, "theirs-a");

        var page = await mine.GetFromJsonAsync<CursorPage<RequestRow>>(FileRequestsApi);

        Assert.NotNull(page);
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, i => Assert.NotEqual("theirs-a", i.Title));
    }

    [Fact]
    public async Task List_RequiresAuth()
    {
        var response = await _factory.CreateClient().GetAsync(FileRequestsApi);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Close_SetsClosedAt()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(providerKey);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var code = await CreateRequestAsync(client, "Close me");

        var page = await client.GetFromJsonAsync<CursorPage<RequestRow>>(FileRequestsApi);
        var id = Assert.Single(page!.Items).Id;

        var response = await client.PostAsync($"{FileRequestsApi}/{id}/close", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await client.GetFromJsonAsync<CursorPage<RequestRow>>(FileRequestsApi);
        var row = Assert.Single(after!.Items);
        Assert.NotNull(row.ClosedAt);
        Assert.Equal(code, row.Code);
    }

    [Fact]
    public async Task Close_OtherUsersRequest_ReturnsNotFound()
    {
        var ownerKey = Guid.CreateVersion7().ToString();
        var otherKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(ownerKey);
        await _factory.SeedUserAsync(otherKey);

        var owner = _factory.CreateAuthenticatedClient(ownerKey);
        var other = _factory.CreateAuthenticatedClient(otherKey);

        await CreateRequestAsync(owner, "not yours");
        var page = await owner.GetFromJsonAsync<CursorPage<RequestRow>>(FileRequestsApi);
        var id = Assert.Single(page!.Items).Id;

        var response = await other.PostAsync($"{FileRequestsApi}/{id}/close", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RequestsPage_DirectGet_RendersRowsWithoutJavascript()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(providerKey);
        var client = _factory.CreateAuthenticatedClient(providerKey);
        await CreateRequestAsync(client, "Direct request");

        var response = await client.GetAsync(RoutePaths.AccountFileRequests);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"requests-body\"", html);
        Assert.Contains("account-delivery__name", html);
        Assert.Contains("Direct request", html);
        Assert.Contains("/r/", html);

        using var targetedRequest = new HttpRequestMessage(HttpMethod.Get, RoutePaths.AccountFileRequests);
        targetedRequest.Headers.Add(UnpolyHeaders.Target, "#requests-body");
        var targeted = await client.SendAsync(targetedRequest);
        Assert.Equal(HttpStatusCode.OK, targeted.StatusCode);
        Assert.Contains("Direct request", await targeted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RequestsPage_EmptyState_WhenNoRequests()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(providerKey);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var html = await client.GetStringAsync(RoutePaths.AccountFileRequests);
        Assert.Contains("account-deliveries-empty", html);
        Assert.DoesNotContain("account-delivery__name", html);
    }

    [Fact]
    public async Task RequestsPage_CursorIsBookmarkableWithAndWithoutUnpoly()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var index = 0; index <= FileRequestPaging.DefaultLimit; index++)
            {
                db.FileRequests.Add(new FileRequest
                {
                    OwnerId = user.Id,
                    Code = Guid.CreateVersion7().ToString("N")[..FileRequest.CodeMaxLength],
                    Title = $"paged-request-{index}",
                    ExpiresAt = DateTime.UtcNow.AddDays(1),
                    MaxFiles = 1,
                });
            }
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateAuthenticatedClient(providerKey);
        var first = await client.GetFromJsonAsync<CursorPage<RequestRow>>(FileRequestsApi);
        Assert.NotNull(first?.NextCursor);
        var firstHtml = await client.GetStringAsync(RoutePaths.AccountFileRequests);
        Assert.Contains("id=\"requests-next-page\"", firstHtml);
        Assert.Contains("up-defer=\"reveal\"", firstHtml);

        var nextUrl = $"{RoutePaths.AccountFileRequests}?cursor={Uri.EscapeDataString(first.NextCursor)}";
        var nextHtml = await client.GetStringAsync(nextUrl);
        Assert.Contains("id=\"requests-body\"", nextHtml);
        Assert.Contains("requests-chunk", nextHtml);
        Assert.Contains("paged-request-", nextHtml);

        using var targetedRequest = new HttpRequestMessage(HttpMethod.Get, nextUrl);
        targetedRequest.Headers.Add(UnpolyHeaders.Target, "#requests-pages");
        var targeted = await client.SendAsync(targetedRequest);
        Assert.Equal(HttpStatusCode.OK, targeted.StatusCode);
        Assert.Contains("id=\"requests-pages\"", await targeted.Content.ReadAsStringAsync());
    }
}
