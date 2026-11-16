using QuickShare.Extensions;

namespace QuickShare.Features.Uploads.AddFile;

public sealed class AddFileRequest
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public IReadOnlyList<int> PartNumbers { get; set; } = [];
}

/// <summary>Presigned parts for one extra file in an in-flight delivery.</summary>
public sealed record AddFileResponse(string UploadId, string ObjectKey, IReadOnlyList<R2PartUrl> Parts);

/// <summary>
/// POST /api/uploads/{DeliveryId}/files — add another file to an upload already in flight (#79).
///
/// The first file goes through <c>start-r2</c>, which is what creates the session; this adds the
/// second through hundredth to the same one. Splitting it that way keeps the single-file path
/// byte-for-byte unchanged — the overwhelmingly common case pays nothing for a feature it does
/// not use — and it means a bundle upload is just N presigned uploads plus one commit.
///
/// Authorisation is the session: only an in-progress session, and only the account that started
/// it, can have files added. Without that check anyone could staple a file onto somebody else's
/// upload and have it delivered under their name.
/// </summary>
public sealed class AddFileEndpoint : Endpoint<AddFileRequest, AddFileResponse>
{
    public override void Configure()
    {
        Post("/api/uploads/{DeliveryId}/files");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Uploads));
        Summary(s =>
        {
            s.Summary = "Add another file to an in-flight upload";
            s.Description = "Returns presigned part URLs. Commit them together via the commit call's additionalFiles.";
        });
    }

    public override async Task HandleAsync(AddFileRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var deliveryId = Route<Guid>("DeliveryId");

        var session = await db.UploadSessions
            .FirstOrDefaultAsync(s => s.Id == deliveryId && s.Status == UploadSessionStatus.InProgress, ct);

        if (session is null)
        {
            ThrowError(ErrorCodes.UploadSessionNotFound, StatusCodes.Status404NotFound);
            return;
        }

        // The session's owner, and nobody else. An anonymous session can only be extended
        // anonymously; a signed-in one only by the account that started it.
        var user = await HttpContext.User.GetUserOrDefaultAsync(db, ct);
        if (session.SenderId != user?.Id)
        {
            ThrowError(ErrorCodes.BadRequest, StatusCodes.Status403Forbidden);
            return;
        }

        var existing = await db.DeliveryFiles.CountAsync(f => f.DeliveryId == deliveryId, ct);
        // +1 for the file the session itself represents, which has no row until commit.
        if (existing + 1 >= DeliveryFile.MaxFilesPerDelivery)
        {
            ThrowError(ErrorCodes.DeliveryTooManyFiles, StatusCodes.Status400BadRequest);
            return;
        }

        if (req.PartNumbers.Count == 0 || req.FileSize < 0)
        {
            ThrowError(ErrorCodes.BadRequest, StatusCodes.Status400BadRequest);
            return;
        }

        var storage = Resolve<IStorageProvider>();
        var name = DeliveryFile.SanitizeName(req.FileName, existing + 1);

        // Same delivery id, so the object lands under the same prefix — which is what makes the
        // orphan sweep, the region routing and the commit-time prefix check all keep working.
        // The region is read back off the first file's key rather than stored again: one source
        // of truth, and it guarantees every file of a bundle lands in the same bucket.
        var region = StorageRegions.FromKey(session.StoragePath ?? string.Empty);
        var init = await storage.InitR2MultipartUploadAsync(deliveryId, name, region, ct);
        var parts = await storage.GenerateUploadPartUrlsAsync(init.UploadId, init.ObjectKey, req.PartNumbers, ct);

        await Send.OkAsync(new AddFileResponse(init.UploadId, init.ObjectKey, parts), ct);
    }
}
