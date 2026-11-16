using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// The download page is the surface strangers open from a chat, often inside an
/// in-app webview. It must deliver the file with no JavaScript at all: every
/// phase used to ship class="hidden" and a script failure left an empty card.
/// </summary>
public sealed class DownloadNoScriptTests : IClassFixture<TestWebAppFactory>
{
    private const string CultureCookieVi = ".AspNetCore.Culture=c=vi|uic=vi";

    private readonly TestWebAppFactory _factory;

    public DownloadNoScriptTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FilePhase_IsVisibleWithoutJavaScript()
    {
        var shareCode = await SeedAsync(hasOtp: false);
        var html = await GetHtmlAsync($"/d/{shareCode}");

        Assert.Contains("id=\"download-phase-file\"", html);
        Assert.DoesNotContain("id=\"download-phase-file\" class=\"hidden\"", html);
        Assert.DoesNotContain("<blazor-ssr", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadAction_IsARealLink_NotAScriptOnlyButton()
    {
        var shareCode = await SeedAsync(hasOtp: false);
        var html = await GetHtmlAsync($"/d/{shareCode}");

        Assert.Contains($"id=\"dl-download-btn\" href=\"/api/downloads/{shareCode}\"", html);
    }

    [Fact]
    public async Task OtpDelivery_ServerRendersTheGate_NotTheFile()
    {
        var shareCode = await SeedAsync(hasOtp: true);
        var html = await GetHtmlAsync($"/d/{shareCode}");

        Assert.DoesNotContain("id=\"download-phase-otp\" class=\"text-center hidden\"", html);
        Assert.Contains("id=\"download-phase-file\" class=\"hidden\"", html);
    }

    [Fact]
    public async Task AdPhase_IsNeverServerRendered()
    {
        // An interstitial nobody can dismiss without JS must not stand between a
        // recipient and their file; JS raises it once the page is already usable.
        var shareCode = await SeedAsync(hasOtp: false);
        var html = await GetHtmlAsync($"/d/{shareCode}");

        Assert.Contains("id=\"download-phase-ad\" class=\"text-center hidden\"", html);
    }

    [Fact]
    public async Task Page_CarriesItsScriptStringsSoTheJsHasNoEnglishLiterals()
    {
        var shareCode = await SeedAsync(hasOtp: false);
        var html = await GetHtmlAsync($"/d/{shareCode}");

        Assert.Contains("id=\"download-i18n\"", html);
        Assert.Contains("previewLoading", html);
    }

    [Fact]
    public async Task VietnameseCulture_TranslatesThePageAndTheLangAttribute()
    {
        var shareCode = await SeedAsync(hasOtp: false);
        var html = await GetHtmlAsync($"/d/{shareCode}", vi: true);

        Assert.Contains("<html lang=\"vi\"", html);
        Assert.Contains("Dung lượng", html);
        Assert.DoesNotContain("Downloads left", html);
    }

    private async Task<string> GetHtmlAsync(string url, bool vi = false)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (vi)
            request.Headers.Add("Cookie", CultureCookieVi);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private async Task<string> SeedAsync(bool hasOtp)
    {
        var shareCode = $"nojs{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Deliveries.Add(new Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = _factory.TestUserId,
            FileName = "ban-ve-mat-bang.pdf",
            ContentType = "application/pdf",
            FileSize = 2048,
            TotalBytes = 2048,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 10,
            StoragePath = $"stub/{shareCode}",
            ExpiresAt = DateTime.UtcNow.AddDays(3),
            OtpHash = hasOtp ? BCrypt.Net.BCrypt.HashPassword("123456") : null,
        });
        await db.SaveChangesAsync();
        return shareCode;
    }
}
