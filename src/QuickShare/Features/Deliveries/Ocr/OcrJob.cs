using Hangfire;

namespace QuickShare.Features.Deliveries.Ocr;

/// <summary>
/// Reads text out of images the sender asked us to index (issue #243).
///
/// A background job, not part of the upload: OCR takes seconds, and an upload that waited for it
/// would trade a feature nobody sees for the one thing every sender does notice. A file is
/// searchable a moment after it lands, which is soon enough for "the invoice with the PO number".
///
/// It only ever touches images whose sender opted in. Privacy mode never reaches here at all —
/// a share that deliberately keeps no record must not grow a searchable transcript of itself,
/// and that gate is applied at commit rather than trusted to this loop.
/// </summary>
public sealed class OcrJob(
    IServiceScopeFactory scopeFactory,
    IStorageProvider storage,
    IOcrEngine ocr,
    ILogger<OcrJob> logger)
{
    /// <summary>Images past this are refused rather than queued: a scan is not a search index.</summary>
    public const long MaxImageBytes = 20L * 1024 * 1024;
    /// <summary>Per tick. Bounded because each item is a process launch, not a query.</summary>
    private const int BatchSize = 10;

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync()
    {
        if (!ocr.IsAvailable)
            return;

        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = await db.Deliveries
            .Where(d => d.OcrRequested && d.DeletedAt == null)
            .Where(d => !db.DeliveryOcrTexts.Any(o => o.DeliveryId == d.Id))
            .OrderBy(d => d.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var delivery in pending)
        {
            var text = await ReadAsync(delivery, ct);

            // A row either way, with null text for "we looked and found nothing". Without it the
            // same unreadable screenshot would be re-processed on every tick, forever.
            db.DeliveryOcrTexts.Add(new DeliveryOcrText
            {
                Id = Guid.CreateVersion7(),
                DeliveryId = delivery.Id,
                Text = text,
                CreatedAt = DateTime.UtcNow,
            });
        }

        if (pending.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    private async Task<string?> ReadAsync(Delivery delivery, CancellationToken ct)
    {
        if (!DeliveryOcrText.IsIndexable(delivery.ContentType)
            || delivery.FileSize > MaxImageBytes
            || string.IsNullOrEmpty(delivery.StoragePath))
        {
            return null;
        }

        try
        {
            var stream = await storage.OpenReadAsync(delivery.StoragePath, ct);
            if (stream is null)
                return null;

            await using (stream)
            {
                var text = await ocr.ReadAsync(stream, TesseractOcrEngine.DefaultLanguages, ct);
                return text is null ? null : TesseractOcrEngine.Normalize(text, DeliveryOcrText.TextMaxLength);
            }
        }
        catch (Exception ex) when (ex is IOException or R2StorageException)
        {
            // One unreadable object must not stop the batch; the null row stops it being retried.
            logger.LogWarning(ex, "OCR could not read delivery {DeliveryId}", delivery.Id);
            return null;
        }
    }
}
