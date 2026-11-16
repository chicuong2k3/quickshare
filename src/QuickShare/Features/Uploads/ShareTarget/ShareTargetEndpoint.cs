namespace QuickShare.Features.Uploads.ShareTarget;

/// <summary>
/// Receives the Android/Chrome Web Share Target POST (issue #14): stashes the
/// shared file with a 15-minute TTL, then redirects to /send?shared={id} so the
/// normal upload flow (progress + resume) runs client-side.
/// </summary>
public sealed class ShareTargetEndpoint : Endpoint<ShareTargetRequest>
{
    public override void Configure()
    {
        Post("/share-target");
        AllowAnonymous();
        AllowFileUploads();
        DontAutoSendResponse();
        Description(b => b.WithTags(ApiTags.Uploads));
        Summary(s =>
        {
            s.Summary = "Receive a file from the system share sheet";
            s.Description = "Stashes the file as a temp upload and redirects to /send?shared={id}.";
        });
    }

    public override async Task HandleAsync(ShareTargetRequest req, CancellationToken ct)
    {
        var file = req.File;
        var id = Guid.CreateVersion7();
        var tempPath = ShareTargetService.TempPathFor(id);

        await using (var stream = System.IO.File.Create(tempPath))
        {
            await file.CopyToAsync(stream, ct);
        }

        var db = Resolve<AppDbContext>();
        db.TempUploads.Add(new TempUpload
        {
            Id = id,
            FileName = file.FileName,
            ContentType = file.ContentType,
            Size = file.Length,
            TempPath = tempPath
        });
        await db.SaveChangesAsync(ct);

        HttpContext.Response.Redirect($"/send?shared={id}", permanent: false);
    }
}

public class ShareTargetRequest
{
    public IFormFile File { get; set; } = null!;
}
