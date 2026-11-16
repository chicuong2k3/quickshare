using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

public sealed class AccountProfileTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public AccountProfileTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task NativeProfileForm_ChangesName()
    {
        const string newName = "Renamed Via Test";
        var userKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(userKey);
        using var client = _factory.CreateAuthenticatedClient(userKey);
        var page = await client.GetStringAsync(RoutePaths.AccountProfile);
        var token = AntiforgeryHelper.ExtractToken(page);
        using var form = ProfileForm(newName, token);

        using var response = await client.PostAsync(RoutePaths.AccountProfile, form);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(newName, html);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.Equal(newName, saved.Name);
    }

    [Fact]
    public async Task NativeProfileForm_EmptyNameShowsError()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var page = await client.GetStringAsync(RoutePaths.AccountProfile);
        using var form = ProfileForm(string.Empty, AntiforgeryHelper.ExtractToken(page));

        using var response = await client.PostAsync(RoutePaths.AccountProfile, form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Name is required", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NativeProfileForm_MissingTokenIsRejected()
    {
        using var client = _factory.CreateAuthenticatedClient();
        using var form = ProfileForm("Unexpected Name", string.Empty);

        using var response = await client.PostAsync(RoutePaths.AccountProfile, form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NativeProfileForm_RejectsOversizedAvatar()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var page = await client.GetStringAsync(RoutePaths.AccountProfile);
        using var form = ProfileForm("Test User", AntiforgeryHelper.ExtractToken(page));
        using var image = new ByteArrayContent(new byte[5 * 1024 * 1024 + 1]);
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(image, "avatar", "large.png");

        using var response = await client.PostAsync(RoutePaths.AccountProfile, form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Image must be under 5 MB", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ProfilePage_Unauthenticated_RedirectsToLogin()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync(RoutePaths.AccountProfile);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.ToString());
    }

    private static MultipartFormDataContent ProfileForm(string name, string token) => new()
    {
        { new StringContent(name), "name" },
        { new StringContent("account-profile"), "_handler" },
        { new StringContent(token), "__RequestVerificationToken" },
    };
}
