namespace QuickShare.Features.Workspaces.Branding;

/// <summary>
/// GET /brand/{WorkspaceId}/{Kind} — serves a brand asset to recipients (issues #285, #286).
///
/// Anonymous, because the audience is the client opening a delivery, who has no account here.
/// That is safe: the response is a logo or a font file the workspace chose to publish, and the
/// workspace id in the URL is not a capability — it grants nothing except these two assets.
///
/// Streamed through the app rather than handed out as a presigned URL. A presigned link expires
/// while the page is still open, and a public bucket is not something a self-hoster necessarily
/// has. The far-future cache header is safe because replacing an asset writes a new key, so the
/// URL changes with the content.
/// </summary>
public sealed class BrandAssetEndpoint : EndpointWithoutRequest
{
    private const string CacheControl = "public, max-age=86400, immutable";

    public override void Configure()
    {
        Get("/brand/{WorkspaceId}/{Kind}");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Serve a workspace brand asset";
            s.Description = "Public. Returns the workspace logo or an uploaded woff2 font for use on recipient-facing pages.";
        });
        DontAutoSendResponse();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var kind = Route<string>("Kind") ?? string.Empty;
        if (!BrandAssetKind.All.Contains(kind) || !Guid.TryParse(Route<string>("WorkspaceId"), out var workspaceId))
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var db = Resolve<AppDbContext>();
        var brand = await db.WorkspaceBrands
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.WorkspaceId == workspaceId, ct);

        // A disabled brand serves nothing: switching it off has to actually take it off the
        // client's page, not just stop referencing it.
        if (brand is null || !brand.Enabled)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var key = UploadBrandAssetEndpoint.KeyOf(brand, kind);
        if (string.IsNullOrEmpty(key))
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var stream = await Resolve<IStorageProvider>().OpenReadAsync(key, ct);
        if (stream is null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await using (stream)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status200OK;
            HttpContext.Response.ContentType = BrandAssetKind.IsFont(kind)
                ? BrandAssets.FontContentType
                : BrandAssets.LogoContentType;
            HttpContext.Response.Headers.CacheControl = CacheControl;
            // The bytes came from an upload we sniffed but did not parse; never let a browser
            // second-guess the type we are declaring.
            HttpContext.Response.Headers.XContentTypeOptions = "nosniff";
            await stream.CopyToAsync(HttpContext.Response.Body, ct);
        }
    }
}
