using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Account pages (issue #6): profile rename via the Htmxor form persists to the DB.
/// Htmxor protocol: the form's hxor-eventid goes back as the HXOR-Event-Handler-Id
/// header, and the HX-XSRF-TOKEN cookie value as the RequestVerificationToken header
/// (mirrors what _content/Htmxor/htmxor.js does in the browser).
/// </summary>
public sealed partial class AccountProfileTests : IClassFixture<TestWebAppFactory>
{
    private const string ProfileUrl = "/account/profile";
    private const string HxRequestHeader = "HX-Request";
    private const string EventHandlerHeader = "HXOR-Event-Handler-Id";
    private const string TokenHeader = "RequestVerificationToken";
    private const string XsrfCookieName = "HX-XSRF-TOKEN";
    private const string NewDisplayName = "Renamed Via Test";

    [GeneratedRegex("""hxor-eventid="([^"]+)""")]
    private static partial Regex EventIdAttr();

    private readonly TestWebAppFactory _factory;

    public AccountProfileTests(TestWebAppFactory factory) => _factory = factory;

    /// <summary>POSTs the profile form through the full Htmxor protocol and returns the response HTML.</summary>
    private async Task<string> SubmitProfileFormAsync(string name)
    {
        // Cookies handled manually so the XSRF cookie value can be read.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, TestWebAppFactory.TestUserProviderKey);

        var getRequest = new HttpRequestMessage(HttpMethod.Get, ProfileUrl);
        getRequest.Headers.Add(HxRequestHeader, "true");
        var getResponse = await client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var eventId = EventIdAttr().Match(await getResponse.Content.ReadAsStringAsync());
        Assert.True(eventId.Success, "hxor-eventid not found in profile form");

        var setCookies = getResponse.Headers.GetValues("Set-Cookie")
            .Select(c => c.Split(';')[0])
            .ToList();
        var xsrfToken = setCookies
            .First(c => c.StartsWith($"{XsrfCookieName}="))[(XsrfCookieName.Length + 1)..];

        var form = new MultipartFormDataContent
        {
            { new StringContent(name), "name" },
        };
        var postRequest = new HttpRequestMessage(HttpMethod.Post, ProfileUrl) { Content = form };
        postRequest.Headers.Add(HxRequestHeader, "true");
        postRequest.Headers.Add(EventHandlerHeader, eventId.Groups[1].Value);
        postRequest.Headers.Add(TokenHeader, Uri.UnescapeDataString(xsrfToken));
        postRequest.Headers.Add("Cookie", string.Join("; ", setCookies));
        var postResponse = await client.SendAsync(postRequest);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        return await postResponse.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task ProfileUpdate_ChangesName_PersistsToDb()
    {
        var html = await SubmitProfileFormAsync(NewDisplayName);
        Assert.Contains(NewDisplayName, html);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == _factory.TestUserId);
        Assert.Equal(NewDisplayName, user.Name);
    }

    [Fact]
    public async Task ProfileUpdate_EmptyName_ShowsValidationError()
    {
        var html = await SubmitProfileFormAsync("");
        Assert.Contains("Name is required", html);
    }

    [Fact]
    public async Task ProfilePage_Unauthenticated_RedirectsToLogin()
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync(ProfileUrl);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.ToString());
    }
}
