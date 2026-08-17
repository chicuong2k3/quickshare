using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
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
    public async Task RequestsPage_LazyLoadsRows()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(providerKey);
        var client = _factory.CreateAuthenticatedClient(providerKey);
        await CreateRequestAsync(client, "Lazy request");

        // Shell render: instant, no rows yet.
        var shell = await client.GetAsync(RoutePaths.AccountFileRequests);
        Assert.Equal(HttpStatusCode.OK, shell.StatusCode);
        var shellHtml = await shell.Content.ReadAsStringAsync();
        Assert.DoesNotContain("account-delivery__name", shellHtml);

        // HTMX fetch: the request row appears with its open status.
        var loaded = await GetHtmxAsync(client, RoutePaths.AccountFileRequests);
        Assert.Contains("account-delivery__name", loaded);
        Assert.Contains("Lazy request", loaded);
        Assert.Contains("/r/", loaded);
    }

    [Fact]
    public async Task RequestsPage_EmptyState_WhenNoRequests()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(providerKey);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var html = await GetHtmxAsync(client, RoutePaths.AccountFileRequests);
        Assert.Contains("account-deliveries-empty", html);
        Assert.DoesNotContain("account-delivery__name", html);
    }

    private static async Task<string> GetHtmxAsync(HttpClient client, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(HtmxHeaders.Request, HtmxHeaders.RequestValue);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
