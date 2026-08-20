using System.IO.Compression;
using LightResults;
using QuickShare.Features.Downloads.Shared;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Features.Downloads.ZipContents;

public record ZipContentsQuery(string ShareCode) : IRequest<Result<IReadOnlyList<ZipEntryResponse>>>;

public record ZipEntryResponse(string Name, long Size, long CompressedSize, bool IsDirectory);

/// <summary>
/// Lists a ZIP's entries before download (issue #215). Reads only the central
/// directory, never the decompressed bytes. Gated like the media preview (#21):
/// password/OTP-gated and exhausted deliveries don't expose their contents.
/// </summary>
public sealed class ZipContentsHandler(IStorageProvider storage, AppDbContext db, DeliveryAccessService access)
    : IRequestHandler<ZipContentsQuery, Result<IReadOnlyList<ZipEntryResponse>>>
{
    private const long MaxZipBytes = 200 * 1024 * 1024;
    private const int MaxZipEntries = 10_000;

    public async ValueTask<Result<IReadOnlyList<ZipEntryResponse>>> Handle(ZipContentsQuery query, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries
            .AsNoTracking()
            .ByCode(query.ShareCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (delivery is null || delivery.DeletedAt is not null)
            return Result.Failure<IReadOnlyList<ZipEntryResponse>>(new LightResults.Error(ErrorCodes.NotFound));

        // Same scope rules as the byte path (issues #142/#166): the entry list is content.
        if (await access.CheckAsync(delivery, cancellationToken) != DeliveryAccessDecision.Allowed)
            return Result.Failure<IReadOnlyList<ZipEntryResponse>>(new LightResults.Error(ErrorCodes.DeliveryRestricted));

        if (delivery.Status is DeliveryStatus.Suspended or DeliveryStatus.Revoked or DeliveryStatus.Expired
            || (delivery.ExpiresAt.HasValue && delivery.ExpiresAt < DateTime.UtcNow))
            return Result.Failure<IReadOnlyList<ZipEntryResponse>>(new LightResults.Error(ErrorCodes.DeliveryExpired));

        if (delivery.DownloadCount >= delivery.MaxDownloads)
            return Result.Failure<IReadOnlyList<ZipEntryResponse>>(new LightResults.Error(ErrorCodes.DownloadLimitReached));

        if (!string.IsNullOrEmpty(delivery.OtpHash) || !string.IsNullOrEmpty(delivery.PasswordHash) || string.IsNullOrEmpty(delivery.StoragePath))
            return Result.Failure<IReadOnlyList<ZipEntryResponse>>(new LightResults.Error(ErrorCodes.DeliveryGated));

        var isZip = string.Equals(delivery.ContentType, "application/zip", StringComparison.OrdinalIgnoreCase)
            || string.Equals(delivery.ContentType, "application/x-zip-compressed", StringComparison.OrdinalIgnoreCase)
            || delivery.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        if (!isZip)
            return Result.Failure<IReadOnlyList<ZipEntryResponse>>(new LightResults.Error(ErrorCodes.NotZip));

        if (delivery.FileSize > MaxZipBytes)
            return Result.Failure<IReadOnlyList<ZipEntryResponse>>(new LightResults.Error(ErrorCodes.ZipTooLarge));

        Stream? stream;
        try
        {
            stream = await storage.OpenReadAsync(delivery.StoragePath, cancellationToken);
        }
        catch
        {
            stream = null;
        }
        if (stream is null)
            return Result.Failure<IReadOnlyList<ZipEntryResponse>>(new LightResults.Error(ErrorCodes.DeliveryNotFound));

        using (stream)
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false))
        {
            // Zip-bomb guard: cap the entry count, not just the archive bytes —
            // a million-entry archive is cheap to create but expensive to list.
            if (zip.Entries.Count > MaxZipEntries)
                return Result.Failure<IReadOnlyList<ZipEntryResponse>>(new LightResults.Error(ErrorCodes.ZipTooManyEntries));

            var entries = zip.Entries
                .Select(e => new ZipEntryResponse(
                    e.FullName,
                    e.Length,
                    e.CompressedLength,
                    e.FullName.EndsWith('/')))
                .ToList();

            return Result.Success<IReadOnlyList<ZipEntryResponse>>(entries);
        }
    }
}

public sealed class ZipContentsEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/downloads/{Code}/zip-contents");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Downloads));
        Summary(s =>
        {
            s.Summary = "List a ZIP delivery's contents before download";
            s.Description = "Issue #215. Gated like the media preview.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var code = Route<string>("Code") ?? string.Empty;
        var result = await Resolve<IMediator>().Send<Result<IReadOnlyList<ZipEntryResponse>>>(
            new ZipContentsQuery(code), ct);

        if (result.IsSuccess(out var entries))
            await Send.OkAsync(entries, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound => 404,
                ErrorCodes.DeliveryExpired => 410,
                ErrorCodes.DeliveryGated => 403,
                ErrorCodes.NotZip or ErrorCodes.ZipTooLarge or ErrorCodes.ZipTooManyEntries => 400,
                _ => 400
            });
    }
}
