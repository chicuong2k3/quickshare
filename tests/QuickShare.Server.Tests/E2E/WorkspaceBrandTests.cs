using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Workspaces.Branding;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Workspace branding on recipient pages (#285) and custom typography (#286).
///
/// The invariants worth defending are mostly about what a brand is *not* allowed to do: it
/// cannot make its own page unreadable, it cannot smuggle CSS or script through a colour or a
/// font name, it cannot serve an SVG to a client's browser, and switching it off has to
/// actually take it off the page rather than merely stop linking to it.
/// </summary>
public sealed class WorkspaceBrandTests : IClassFixture<TestWebAppFactory>
{
    private static readonly byte[] PngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 1, 2, 3];

    private static readonly byte[] Woff2Bytes =
        [(byte)'w', (byte)'O', (byte)'F', (byte)'2', 0, 0, 0, 0, 1, 2, 3, 4];

    private readonly TestWebAppFactory _factory;

    public WorkspaceBrandTests(TestWebAppFactory factory) => _factory = factory;

    // ── Colours and copy (#285) ──

    [Fact]
    public async Task Brand_SavesColoursAndCopy()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();

        var response = await client.PutAsJsonAsync("/api/workspace/brand", new BrandRequest
        {
            Enabled = true,
            AccentColor = "#c8a24a",
            AccentTextColor = "#101010",
            HeaderText = "Sent by Studio Nam",
            FooterText = "studionam.vn",
            HideQuickShareBranding = true,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(dto);
        Assert.True(dto.Enabled);
        Assert.Equal("#c8a24a", dto.AccentColor);
        Assert.True(dto.HideQuickShareBranding);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var brand = await db.WorkspaceBrands.AsNoTracking().SingleAsync(b => b.WorkspaceId == workspaceId);
        Assert.Equal("Sent by Studio Nam", brand.HeaderText);
    }

    [Fact]
    public async Task Brand_RefusesAColourThatIsNotHex()
    {
        var (client, _) = await WorkspaceClientAsync();

        // Refused rather than coerced: a colour that silently becomes black is worse than a
        // form error, and this is also the shape an injection attempt would take.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/workspace/brand",
                new BrandRequest { AccentColor = "red; } body { display:none" })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/workspace/brand",
                new BrandRequest { AccentColor = "#12345" })).StatusCode);
    }

    [Fact]
    public async Task Brand_RefusesAnUnreadableAccentPair()
    {
        var (client, _) = await WorkspaceClientAsync();

        var response = await client.PutAsJsonAsync("/api/workspace/brand", new BrandRequest
        {
            Enabled = true,
            AccentColor = "#fefefe",
            AccentTextColor = "#ffffff",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void Brand_ContrastMathMatchesTheWcagFormula()
    {
        // Black on white is the maximum, 21:1.
        Assert.Equal(21.0, BrandAssets.ContrastRatio("#000000", "#ffffff"), 1);
        // Shorthand expands to the same colour as the long form.
        Assert.Equal(
            BrandAssets.ContrastRatio("#000000", "#ffffff"),
            BrandAssets.ContrastRatio("#000", "#fff"),
            3);
        Assert.True(BrandAssets.ContrastRatio("#ffffff", "#fefefe") < BrandAssets.MinContrastRatio);
    }

    [Fact]
    public async Task Brand_RefusesAFontNameThatCouldEscapeTheDeclaration()
    {
        var (client, _) = await WorkspaceClientAsync();

        var response = await client.PutAsJsonAsync("/api/workspace/brand", new BrandRequest
        {
            HeadingFontName = "Inter\";} body{display:none;} @font-face{font-family:\"x",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Assets (#285 logo, #286 fonts) ──

    [Fact]
    public async Task Brand_AcceptsAPngLogoAndServesItPublicly()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();
        await EnableAsync(client);

        var upload = await UploadAsync(client, BrandAssetKind.Logo, "logo.png", "image/png", PngBytes);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        // Anonymous, because the audience is a client with no account here.
        var anonymous = _factory.CreateClient();
        var served = await anonymous.GetAsync($"/brand/{workspaceId:N}/{BrandAssetKind.Logo}");

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(BrandAssets.LogoContentType, served.Content.Headers.ContentType?.MediaType);
        // The bytes were sniffed but never parsed, so the browser must not second-guess us.
        Assert.Contains("nosniff", served.Headers.GetValues("X-Content-Type-Options"));
    }

    [Fact]
    public async Task Brand_RefusesAnSvgLogo()
    {
        var (client, _) = await WorkspaceClientAsync();
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

        // SVG is a document format that can carry script; a logo is not worth an XSS surface
        // on a page somebody's client opens.
        var upload = await UploadAsync(client, BrandAssetKind.Logo, "logo.svg", "image/svg+xml", svg);

        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
    }

    [Fact]
    public async Task Brand_RefusesAFontThatIsNotWoff2()
    {
        var (client, _) = await WorkspaceClientAsync();
        var ttf = new byte[] { 0x00, 0x01, 0x00, 0x00, 0, 0, 0, 0 };

        // The declared content type says woff2; the bytes say otherwise, and the bytes win.
        var upload = await UploadAsync(client, BrandAssetKind.HeadingFont, "font.ttf", "font/woff2", ttf);

        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
    }

    [Fact]
    public async Task Brand_AcceptsAWoff2FontAndServesIt()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();
        await EnableAsync(client);

        var upload = await UploadAsync(client, BrandAssetKind.BodyFont, "body.woff2", "font/woff2", Woff2Bytes);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        var anonymous = _factory.CreateClient();
        var served = await anonymous.GetAsync($"/brand/{workspaceId:N}/{BrandAssetKind.BodyFont}");

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(BrandAssets.FontContentType, served.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Brand_ReplacingAnAssetChangesItsKey()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();

        await UploadAsync(client, BrandAssetKind.Logo, "logo.png", "image/png", PngBytes);
        var first = await LogoKeyAsync(workspaceId);

        await UploadAsync(client, BrandAssetKind.Logo, "logo2.png", "image/png", PngBytes);
        var second = await LogoKeyAsync(workspaceId);

        // The public route carries a far-future cache header, so an in-place overwrite would
        // leave a client's browser showing last month's logo.
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Brand_DisabledServesNothingPublicly()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();
        await EnableAsync(client);
        await UploadAsync(client, BrandAssetKind.Logo, "logo.png", "image/png", PngBytes);

        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK,
            (await anonymous.GetAsync($"/brand/{workspaceId:N}/{BrandAssetKind.Logo}")).StatusCode);

        await client.PutAsJsonAsync("/api/workspace/brand", new BrandRequest { Enabled = false });

        // Switching a brand off has to take it off the page, not just stop referencing it.
        Assert.Equal(HttpStatusCode.NotFound,
            (await anonymous.GetAsync($"/brand/{workspaceId:N}/{BrandAssetKind.Logo}")).StatusCode);
    }

    [Fact]
    public async Task Brand_AssetRouteIs404ForAnUnknownWorkspaceOrKind()
    {
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound,
            (await anonymous.GetAsync($"/brand/{Guid.NewGuid():N}/{BrandAssetKind.Logo}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await anonymous.GetAsync($"/brand/{Guid.NewGuid():N}/passwords")).StatusCode);
    }

    [Fact]
    public async Task Brand_IsAdminOnly()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        var seatless = _factory.CreateAuthenticatedClient(providerKey);

        Assert.Equal(HttpStatusCode.Forbidden, (await seatless.GetAsync("/api/workspace/brand")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await seatless.PutAsJsonAsync("/api/workspace/brand", new BrandRequest())).StatusCode);
    }

    // ── Rendering (#285, #286) ──

    [Fact]
    public void Brand_CssOverridesValuesOnlyAndKeepsAFallbackStack()
    {
        var view = new BrandView(
            Guid.NewGuid(),
            HasLogo: true,
            AccentColor: "#c8a24a",
            AccentTextColor: "#101010",
            HeaderText: "Studio Nam",
            FooterText: null,
            HideQuickShareBranding: true,
            HeadingFontName: "Vollkorn",
            BodyFontName: null,
            HasHeadingFont: true,
            HasBodyFont: false);

        var css = BrandReader.ToCss(view);

        Assert.Contains("--db-color-accent:#c8a24a", css, StringComparison.Ordinal);
        Assert.Contains("@font-face", css, StringComparison.Ordinal);
        // swap, so the download button is never hidden behind a font request that may never
        // finish; and a fallback stack, so the page is readable if it fails outright.
        Assert.Contains("font-display:swap", css, StringComparison.Ordinal);
        Assert.Contains("system-ui", css, StringComparison.Ordinal);
        // Type sizes belong to DESIGN.md; a brand only swaps families.
        Assert.DoesNotContain("font-size", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Brand_AppearsOnTheRecipientPageOfAWorkspaceDelivery()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();
        await client.PutAsJsonAsync("/api/workspace/brand", new BrandRequest
        {
            Enabled = true,
            AccentColor = "#c8a24a",
            HeaderText = "Sent by Studio Nam",
            FooterText = "studionam.vn",
        });

        var shareCode = await SeedDeliveryAsync(workspaceId);

        var anonymous = _factory.CreateClient();
        var page = await anonymous.GetStringAsync($"/d/{shareCode}");

        Assert.Contains("Sent by Studio Nam", page, StringComparison.Ordinal);
        Assert.Contains("studionam.vn", page, StringComparison.Ordinal);
        Assert.Contains("--db-color-accent:#c8a24a", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Brand_DoesNotLeakOntoAPersonalDeliveryPage()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();
        await client.PutAsJsonAsync("/api/workspace/brand", new BrandRequest
        {
            Enabled = true,
            HeaderText = "Sent by Studio Nam",
        });

        // Same sender, no workspace on the delivery: a personal share is not the agency's.
        var shareCode = await SeedDeliveryAsync(workspaceId: null);

        var anonymous = _factory.CreateClient();
        var page = await anonymous.GetStringAsync($"/d/{shareCode}");

        Assert.DoesNotContain("Sent by Studio Nam", page, StringComparison.Ordinal);
    }

    // ── Helpers ──

    private async Task<(HttpClient Client, Guid WorkspaceId)> WorkspaceClientAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = $"Brand {Guid.NewGuid():N}"[..20], Seats = 3 });
        response.EnsureSuccessStatusCode();
        var workspace = (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;

        return (client, workspace.Id);
    }

    private static async Task EnableAsync(HttpClient client) =>
        (await client.PutAsJsonAsync("/api/workspace/brand",
            new BrandRequest { Enabled = true, HeaderText = "Brand on" })).EnsureSuccessStatusCode();

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, string kind, string fileName, string contentType, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(part, "file", fileName);

        return await client.PostAsync($"/api/workspace/brand/assets/{kind}", content);
    }

    private async Task<string?> LogoKeyAsync(Guid workspaceId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.WorkspaceBrands.AsNoTracking()
            .Where(b => b.WorkspaceId == workspaceId)
            .Select(b => b.LogoKey)
            .SingleAsync();
    }

    private async Task<string> SeedDeliveryAsync(Guid? workspaceId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var shareCode = Guid.NewGuid().ToString("N")[..12];
        db.Deliveries.Add(new Delivery
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            FileName = "contract.pdf",
            ContentType = "application/pdf",
            FileSize = 2048,
            TotalBytes = 2048,
            ShareCode = shareCode,
            StoragePath = $"test/{Guid.NewGuid():N}",
            Status = DeliveryStatus.Active,
            MaxDownloads = 10,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        await db.SaveChangesAsync();

        return shareCode;
    }
}
