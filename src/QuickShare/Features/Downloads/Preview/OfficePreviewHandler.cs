using LightResults;
using QuickShare.Features.Downloads.Shared;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Features.Downloads.Preview;

public record OfficePreviewQuery(string ShareCode) : IRequest<Result<OfficePreviewResponse>>;

public record OfficePreviewResponse(string Html, string Format);

/// <summary>
/// Server-side office document preview (issue #94): opens the object stream, extracts
/// text from docx/xlsx/pptx, and returns escaped HTML for the download page preview tab.
/// </summary>
public sealed class OfficePreviewHandler(IStorageProvider storage, AppDbContext db, DeliveryAccessService access)
    : IRequestHandler<OfficePreviewQuery, Result<OfficePreviewResponse>>
{
    private const long MaxPreviewBytes = 20 * 1024 * 1024;

    public async ValueTask<Result<OfficePreviewResponse>> Handle(
        OfficePreviewQuery query, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries
            .AsNoTracking()
            .ByCode(query.ShareCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (delivery is null || delivery.DeletedAt is not null)
            return Result.Failure<OfficePreviewResponse>(new LightResults.Error(ErrorCodes.NotFound));

        // Same scope rules as the byte path (issues #142/#166): a preview is the file.
        if (await access.CheckAsync(delivery, cancellationToken) != DeliveryAccessDecision.Allowed)
            return Result.Failure<OfficePreviewResponse>(new LightResults.Error(ErrorCodes.DeliveryRestricted));

        if (delivery.Status is DeliveryStatus.Suspended or DeliveryStatus.Revoked or DeliveryStatus.Expired
            || (delivery.ExpiresAt.HasValue && delivery.ExpiresAt < DateTime.UtcNow))
            return Result.Failure<OfficePreviewResponse>(new LightResults.Error(ErrorCodes.DeliveryExpired));

        if (!string.IsNullOrEmpty(delivery.OtpHash) || !string.IsNullOrEmpty(delivery.PasswordHash) || string.IsNullOrEmpty(delivery.StoragePath))
            return Result.Failure<OfficePreviewResponse>(new LightResults.Error(ErrorCodes.DeliveryGated));

        var format = OfficeTextExtractor.FormatFor(delivery.ContentType, delivery.FileName);
        if (format is null)
            return Result.Failure<OfficePreviewResponse>(new LightResults.Error(ErrorCodes.NotZip));

        if (delivery.FileSize > MaxPreviewBytes)
            return Result.Failure<OfficePreviewResponse>(new LightResults.Error(ErrorCodes.ZipTooLarge));

        var stream = await storage.OpenReadAsync(delivery.StoragePath, cancellationToken);
        if (stream is null)
            return Result.Failure<OfficePreviewResponse>(new LightResults.Error(ErrorCodes.NotFound));

        string html;
        using (stream)
        {
            try
            {
                html = OfficeTextExtractor.ExtractHtml(stream, format);
            }
            catch (InvalidDataException)
            {
                return Result.Failure<OfficePreviewResponse>(new LightResults.Error(ErrorCodes.NotZip));
            }
        }

        return Result.Success(new OfficePreviewResponse(html, format));
    }
}
