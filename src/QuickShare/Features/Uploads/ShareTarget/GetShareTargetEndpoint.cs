namespace QuickShare.Features.Uploads.ShareTarget;

/// <summary>
/// Hands a stashed share-target file to /send (issue #14). Serves the temp
/// bytes with the original filename; expired or missing files are 404.
/// </summary>
public sealed class GetShareTargetEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/share-target/{Id}");
        AllowAnonymous();
        DontAutoSendResponse();
        Description(b => b.WithTags(ApiTags.Uploads));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var db = Resolve<AppDbContext>();

        var temp = await db.TempUploads
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (temp is null
            || temp.CreatedAt < DateTime.UtcNow.AddMinutes(-TempUpload.TtlMinutes)
            || !System.IO.File.Exists(temp.TempPath))
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        HttpContext.Response.ContentType = string.IsNullOrEmpty(temp.ContentType)
            ? "application/octet-stream"
            : temp.ContentType;
        HttpContext.Response.Headers.ContentDisposition =
            $"attachment; filename*=UTF-8''{Uri.EscapeDataString(temp.FileName)}";
        await HttpContext.Response.SendFileAsync(temp.TempPath, ct);
    }
}
