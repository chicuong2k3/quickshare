using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Browser.Tests;

/// <summary>
/// Download page gates in a real browser (issue #4): file card, ad interstitial
/// for free senders, OTP gate wrong/correct, expired and not-found states.
/// </summary>
public sealed class DownloadPageTests : IClassFixture<BrowserFixture>
{
    private const string TestOtp = "123456";
    private const string WrongOtp = "000000";

    private const string PatchBody = """
        diff --git a/readme.txt b/readme.txt
        index 1111111..2222222 100644
        --- a/readme.txt
        +++ b/readme.txt
        @@ -1,3 +1,3 @@
         hello
        -world
        +world!
         same line
        """;

    private readonly BrowserFixture _fx;

    public DownloadPageTests(BrowserFixture fx)
    {
        _fx = fx;
    }

    private async Task<string> SeedDeliveryAsync(string? senderPlan = null, string? otp = null, bool expired = false, string? fileName = null, string? contentType = null)
    {
        var shareCode = $"bt-{Guid.NewGuid():N}"[..12];
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Guid? senderId = null;
        if (senderPlan is not null)
        {
            var sender = new User
            {
                Id = Guid.CreateVersion7(),
                Name = "Seed Sender",
                Email = $"sender-{Guid.NewGuid():N}@quickshare.test",
                Plan = senderPlan,
            };
            db.Users.Add(sender);
            senderId = sender.Id;
        }

        db.Deliveries.Add(new Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = senderId,
            FileName = fileName ?? "browser-test.pdf",
            ContentType = contentType ?? "application/pdf",
            FileSize = 2048,
            TotalBytes = 2048,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 10,
            StoragePath = $"stub/{shareCode}",
            ExpiresAt = expired ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddDays(7),
            OtpHash = otp is null ? null : BCrypt.Net.BCrypt.HashPassword(otp),
        });
        await db.SaveChangesAsync();
        return shareCode;
    }

    private async Task<IPage> OpenAsync(string path)
    {
        var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fx.BaseUrl + path);
        return page;
    }

    [Fact]
    public async Task ProSender_ShowsFileCardDirectly()
    {
        var code = await SeedDeliveryAsync(senderPlan: PlanTierLimits.Pro);
        var page = await OpenAsync($"/d/{code}");

        await Assertions.Expect(page.Locator("#download-phase-file")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("browser-test.pdf");
        await Assertions.Expect(page.Locator("#dl-download-btn")).ToBeVisibleAsync();
        // No gate phases for a paid sender.
        await Assertions.Expect(page.Locator("#download-phase-ad")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#download-phase-otp")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task FreeSender_ShowsAdCountdown_SkipRevealsFile()
    {
        var code = await SeedDeliveryAsync(senderPlan: PlanTierLimits.Free);
        var page = await OpenAsync($"/d/{code}");

        await Assertions.Expect(page.Locator("#download-phase-ad")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Assertions.Expect(page.Locator("#dl-ad-countdown")).ToContainTextAsync("Continue in");
        await Assertions.Expect(page.Locator("#download-phase-file")).ToBeHiddenAsync();

        await page.ClickAsync("[data-ai-action='download.ad.skip']");
        await Assertions.Expect(page.Locator("#download-phase-file")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task OtpGate_WrongCodeErrors_CorrectCodeUnlocks()
    {
        var code = await SeedDeliveryAsync(otp: TestOtp);
        var page = await OpenAsync($"/d/{code}");

        await Assertions.Expect(page.Locator("#download-phase-otp")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        await FillOtpAsync(page, WrongOtp);
        await page.ClickAsync("#dl-otp-btn");
        await Assertions.Expect(page.Locator("#dl-otp-error")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#download-phase-file")).ToBeHiddenAsync();

        await FillOtpAsync(page, TestOtp);
        await page.ClickAsync("#dl-otp-btn");
        await Assertions.Expect(page.Locator("#download-phase-file")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#download-phase-otp")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task ExpiredDelivery_ShowsGracefulExpiredPage()
    {
        var code = await SeedDeliveryAsync(expired: true);
        var page = await OpenAsync($"/d/{code}");

        await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("expired",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000, IgnoreCase = true });
    }

    [Fact]
    public async Task UnknownCode_ShowsGracefulNotFoundPage()
    {
        var page = await OpenAsync("/d/does-not-exist");

        await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("This link",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    }

    [Fact]
    public async Task PatchFile_RendersDiffPreview_WithModesAndRawFallback()
    {
        var code = await SeedDeliveryAsync(senderPlan: PlanTierLimits.Pro, fileName: "sample.patch", contentType: "application/octet-stream");
        var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();
        // Preview fetches /api/downloads/{code}; intercept and serve the patch body.
        await page.RouteAsync("**/api/downloads/**", route =>
            route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "text/plain",
                Body = PatchBody,
            }));
        await page.GotoAsync(_fx.BaseUrl + $"/d/{code}");

        await Assertions.Expect(page.Locator("#download-phase-file")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Assertions.Expect(page.Locator("#dl-text-preview")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#dl-diff-toolbar")).ToBeVisibleAsync();

        // Unified mode: add/del rows with +/- markers and line numbers.
        var addRow = page.Locator(".dl-diff__row--add");
        var delRow = page.Locator(".dl-diff__row--del");
        await Assertions.Expect(addRow).ToHaveCountAsync(1);
        await Assertions.Expect(delRow).ToHaveCountAsync(1);
        await Assertions.Expect(addRow).ToContainTextAsync("world!");
        await Assertions.Expect(delRow).ToContainTextAsync("world");
        await Assertions.Expect(page.Locator(".dl-diff__row--del .dl-diff__ln").First).ToHaveTextAsync("2");
        await Assertions.Expect(page.Locator(".dl-diff__marker")).ToHaveCountAsync(4);

        // Split mode: old | new side-by-side pairs.
        await page.ClickAsync("#dl-diff-mode-btn");
        await Assertions.Expect(page.Locator(".dl-diff__pair")).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator(".dl-diff__side--del")).ToContainTextAsync("world");
        await Assertions.Expect(page.Locator(".dl-diff__side--add")).ToContainTextAsync("world!");

        // Raw fallback: plain text, toolbar stays, mode button hides.
        await page.ClickAsync("#dl-diff-raw-btn");
        await Assertions.Expect(page.Locator(".dl-diff__row--add")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#dl-text-body")).ToContainTextAsync("world!");
        await Assertions.Expect(page.Locator("#dl-diff-mode-btn")).ToBeHiddenAsync();
    }

    private static async Task FillOtpAsync(IPage page, string otp)
    {
        var inputs = page.Locator("#dl-otp .db-otp__input");
        for (var i = 0; i < otp.Length; i++)
            await inputs.Nth(i).FillAsync(otp[i].ToString());
    }
}
