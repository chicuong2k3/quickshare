using TL;

namespace TeleCloud.Server.Features.Files.Download;

public record DownloadFileQuery(Guid Id, bool Stream = false, string? RangeHeader = null);

public sealed class DownloadFileHandler(TelegramService telegram)
{
    public async Task HandleAsync(DownloadFileQuery query, IHttpContextAccessor accessor, TeleCloudDbContext db, CancellationToken ct)
    {
        var httpContext = accessor.HttpContext!;
        var channel = await telegram.GetChannelAsync(ct);

        var meta = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == query.Id, ct);
        if (meta is null)
        {
            httpContext.Response.StatusCode = 404;
            return;
        }

        var response = httpContext.Response;
        response.ContentType = meta.ContentType;

        if (!query.Stream)
            response.Headers.ContentDisposition = $"attachment; filename=\"{meta.Filename}\"";

        var docLocation = await RefreshDocumentLocation(meta, channel);
        var (rangeFrom, rangeTo) = ParseRangeHeader(query.RangeHeader, meta.Size);

        if (rangeFrom.HasValue)
        {
            var to = rangeTo ?? meta.Size - 1;
            response.StatusCode = 206;
            response.Headers.ContentRange = $"bytes {rangeFrom}-{to}/{meta.Size}";
            response.ContentLength = to - rangeFrom.Value + 1;
            await StreamBytes(docLocation, response.Body, (int)rangeFrom.Value, (int)(to - rangeFrom.Value + 1), ct);
        }
        else
        {
            response.ContentLength = meta.Size;
            await StreamBytes(docLocation, response.Body, 0, ct);
        }
    }

    private async Task<InputDocumentFileLocation> RefreshDocumentLocation(FileMetadata meta, StorageChannel channel)
    {
        var inputChannel = channel.ToInputChannel();
        var messages = await telegram.Client.Channels_GetMessages(inputChannel, [meta.MessageId]);

        if (messages.Messages.Length == 0 || messages.Messages[0] is not Message msg)
            throw new InvalidOperationException($"File message {meta.MessageId} not found");

        if (msg.media is not MessageMediaDocument { document: Document doc })
            throw new InvalidOperationException("Message is not a document");

        return new InputDocumentFileLocation
        {
            id = doc.ID,
            access_hash = doc.access_hash,
            file_reference = doc.file_reference ?? meta.FileReference,
            thumb_size = ""
        };
    }

    private async Task StreamBytes(InputDocumentFileLocation docLocation, Stream output, int offset, int requestedLength, CancellationToken ct)
    {
        var remaining = requestedLength;
        while (remaining > 0 && !ct.IsCancellationRequested)
        {
            var fileBase = await telegram.Client.Upload_GetFile(docLocation, offset, remaining);
            if (fileBase is not Upload_File file)
                break;

            await output.WriteAsync(file.bytes, ct);
            offset += file.bytes.Length;
            remaining -= file.bytes.Length;

            if (file.bytes.Length == 0)
                break;
        }
    }

    private async Task StreamBytes(InputDocumentFileLocation docLocation, Stream output, int startOffset, CancellationToken ct)
    {
        var offset = startOffset;
        while (!ct.IsCancellationRequested)
        {
            var fileBase = await telegram.Client.Upload_GetFile(docLocation, offset, 524288);
            if (fileBase is not Upload_File file)
                break;

            await output.WriteAsync(file.bytes, ct);
            offset += file.bytes.Length;

            if (file.bytes.Length < 524288)
                break;
        }
    }

    private static (long? from, long? to) ParseRangeHeader(string? header, long fileSize)
    {
        if (string.IsNullOrEmpty(header) || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            return (null, null);

        var rangeValue = header[6..];
        var parts = rangeValue.Split('-');
        if (parts.Length != 2)
            return (null, null);

        var hasFrom = long.TryParse(parts[0], out var from);
        var hasTo = long.TryParse(parts[1], out var to);

        if (!hasFrom && !hasTo)
            return (null, null);

        if (hasFrom && hasTo)
        {
            if (from > to || from >= fileSize)
                return (null, null);
            to = Math.Min(to, fileSize - 1);
        }
        else if (hasFrom)
        {
            to = fileSize - 1;
        }
        else
        {
            from = fileSize - to;
            to = fileSize - 1;
            if (from < 0) from = 0;
        }

        return (from, to);
    }
}
