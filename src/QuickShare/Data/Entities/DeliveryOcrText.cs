namespace QuickShare.Data.Entities;

/// <summary>
/// Text read out of an image so it can be searched (issue #243).
///
/// A separate table rather than a column on <see cref="Delivery"/>: the delivery row is read on
/// every list, every download and every recipient page, and a kilobyte of scanned invoice riding
/// along on all of them would be paid for constantly to serve the one query that wants it.
///
/// A row with <see cref="Text"/> null means "we looked and found nothing" — that is the record
/// that stops the same unreadable screenshot being re-processed on every tick.
/// </summary>
public record DeliveryOcrText : BaseAuditEntity
{
    /// <summary>Enough for a dense page. Past this it is a document, not a screenshot.</summary>
    public const int TextMaxLength = 8000;

    public Guid DeliveryId { get; set; }
    public string? Text { get; set; }

    public Delivery Delivery { get; init; } = null!;

    /// <summary>
    /// Only raster images. PDFs carry their own text layer and would need a different path;
    /// anything else has no picture of words in it to read.
    /// </summary>
    public static bool IsIndexable(string? contentType) =>
        contentType is not null
        && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
        && !contentType.Contains("svg", StringComparison.OrdinalIgnoreCase);
}
