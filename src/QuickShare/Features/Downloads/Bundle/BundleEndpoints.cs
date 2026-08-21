using System.IO.Compression;
using System.IO.Pipelines;
using QuickShare.Features.Downloads.Shared;

namespace QuickShare.Features.Downloads.Bundle;

/// <summary>One file of a delivery, as the recipient page lists it.</summary>
public sealed record BundleFileDto(Guid Id, string FileName, string ContentType, long FileSize, int SortOrder);

/// <summary>
/// GET /api/downloads/{ShareCode}/files — the files inside a delivery (issue #79).
///
/// Goes through the same <see cref="DeliveryAccessService"/> gate as every other recipient-facing
/// read. A file list is not metadata-free: it leaks names, sizes and folder structure, which is
/// exactly the information a password or an IP rule was put there to withhold.
/// </summary>
public sealed class ListBundleFilesEndpoint : EndpointWithoutRequest<IReadOnlyList<BundleFileDto>>
{
    public override void Configure()
    {
        Get("/api/downloads/{ShareCode}/files");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Downloads));
        Summary(s =>
        {
            s.Summary = "List the files in a delivery";
            s.Description = "Subject to the same access rules as the download itself.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var delivery = await BundleAccess.ResolveAsync(db, Resolve<DeliveryAccessService>(), Route<string>("ShareCode"), ct);

        if (delivery is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var files = await db.DeliveryFiles
            .AsNoTracking()
            .Where(f => f.DeliveryId == delivery.Id)
            .OrderBy(f => f.SortOrder)
            .Select(f => new BundleFileDto(f.Id, f.FileName, f.ContentType, f.FileSize, f.SortOrder))
            .ToListAsync(ct);

        await Send.OkAsync(files, ct);
    }
}

/// <summary>
/// GET /api/downloads/{ShareCode}/files/{FileId} — one file of a bundle (issue #79).
///
/// Redirects to a short-lived presigned URL, the same way the single-file download does, so the
/// bytes never travel through this server.
/// </summary>
public sealed class DownloadBundleFileEndpoint : EndpointWithoutRequest
{
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(5);

    public override void Configure()
    {
        Get("/api/downloads/{ShareCode}/files/{FileId}");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Downloads));
        Summary(s => s.Summary = "Download one file from a delivery");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var delivery = await BundleAccess.ResolveAsync(db, Resolve<DeliveryAccessService>(), Route<string>("ShareCode"), ct);

        if (delivery is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // Scoped by delivery, never by file id alone: a file id from one share must not resolve
        // against another's access rules.
        var file = await db.DeliveryFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == Route<Guid>("FileId") && f.DeliveryId == delivery.Id, ct);

        if (file is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var url = await Resolve<IStorageProvider>().GetPresignedDownloadUrlAsync(file.ObjectKey, LinkLifetime, ct);
        await Send.RedirectAsync(url.OriginalString, isPermanent: false, allowRemoteRedirects: true);
    }
}

/// <summary>
/// GET /api/downloads/{ShareCode}/zip — every file, as one archive (issue #79, #43).
///
/// Streamed, not buffered: a bundle can be tens of gigabytes, and building the archive in memory
/// or on disk first would mean the server's headroom decides how large a delivery may be.
/// Entries are stored, not deflated — the payloads are usually already-compressed media, so
/// compressing them again spends CPU to make the file marginally bigger, and it also lets the
/// bytes flow straight from storage to the client.
///
/// The consequence is honest and worth stating: with no Content-Length the browser cannot show a
/// progress bar, and an error partway through arrives as a truncated archive rather than an HTTP
/// status. That is the trade every streaming-zip endpoint makes, and it beats the alternative of
/// capping bundle size at whatever fits in RAM.
/// </summary>
public sealed class DownloadBundleZipEndpoint : EndpointWithoutRequest
{
    private const string ZipContentType = "application/zip";

    public override void Configure()
    {
        Get("/api/downloads/{ShareCode}/zip");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Downloads));
        Summary(s =>
        {
            s.Summary = "Download every file in a delivery as one zip";
            s.Description = "Streamed and stored (not deflated). No Content-Length — the archive is produced as it is sent.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var delivery = await BundleAccess.ResolveAsync(db, Resolve<DeliveryAccessService>(), Route<string>("ShareCode"), ct);

        if (delivery is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var files = await db.DeliveryFiles
            .AsNoTracking()
            .Where(f => f.DeliveryId == delivery.Id)
            .OrderBy(f => f.SortOrder)
            .ToListAsync(ct);

        if (files.Count == 0)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var storage = Resolve<IStorageProvider>();
        var logger = Resolve<ILogger<DownloadBundleZipEndpoint>>();
        var archiveName = BundleAccess.ArchiveName(delivery);

        // The archive is produced into a pipe and streamed out of the reader end.
        //
        // Writing straight to Response.Body does not work here — the framework still owns the
        // response, so those bytes are discarded and the caller gets an empty 200 that looks
        // exactly like a corrupt archive. Handing it a stream keeps the framework in charge of
        // the response while the pipe keeps the memory bounded: entries flow storage → pipe →
        // client, and at no point does the whole bundle exist anywhere.
        var pipe = new Pipe();

        var producer = Task.Run(async () =>
        {
            try
            {
                await using var output = pipe.Writer.AsStream(leaveOpen: true);
                using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var file in files)
                {
                    // Two files can legitimately share a name — different folders, or a sender
                    // who picked the same one twice. A zip with duplicate entries extracts to a
                    // single file in most tools, silently losing the rest.
                    var entryName = BundleAccess.UniqueEntryName(file.FileName, used);

                    await using var source = await storage.OpenReadAsync(file.ObjectKey, ct);
                    if (source is null)
                    {
                        // The row says the object exists and the bucket disagrees. Skipping keeps
                        // the rest of the archive usable instead of aborting mid-stream, and the
                        // entry's absence is visible to whoever opens it.
                        logger.LogError("Bundle file {Key} missing from storage for delivery {DeliveryId}",
                            file.ObjectKey, delivery.Id);
                        continue;
                    }

                    var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
                    entry.LastWriteTime = file.CreatedAt;

                    await using var entryStream = entry.Open();
                    await source.CopyToAsync(entryStream, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Bundle zip failed for delivery {DeliveryId}", delivery.Id);
                // Faulting the pipe truncates the download rather than completing a half-archive
                // that looks whole. A truncated zip fails loudly in every extractor.
                await pipe.Writer.CompleteAsync(ex);
                return;
            }

            await pipe.Writer.CompleteAsync();
        }, ct);

        await Send.StreamAsync(
            pipe.Reader.AsStream(),
            fileName: archiveName,
            fileLengthBytes: null,
            contentType: ZipContentType,
            cancellation: ct);

        await producer;
    }
}

/// <summary>Shared access resolution and naming for the bundle endpoints.</summary>
internal static class BundleAccess
{
    private const int ArchiveNameMaxLength = 80;

    /// <summary>
    /// The delivery behind a share code, or null when the caller may not have it.
    ///
    /// One place, used by all three endpoints, because a gate applied in two of three is not a
    /// gate — and "the list endpoint forgot the password check" is exactly the kind of hole that
    /// gets found by somebody who is not us.
    /// </summary>
    public static async Task<Delivery?> ResolveAsync(
        AppDbContext db, DeliveryAccessService access, string? shareCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(shareCode))
            return null;

        var delivery = await db.Deliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ShareCode == shareCode && d.DeletedAt == null, ct);

        if (delivery is null || delivery.Status != DeliveryStatus.Active)
            return null;

        if (delivery.ExpiresAt is { } expiresAt && expiresAt < DateTime.UtcNow)
            return null;

        // A password- or OTP-gated share is not listable either: names and sizes are the
        // information the gate exists to withhold.
        if (!string.IsNullOrEmpty(delivery.PasswordHash) || !string.IsNullOrEmpty(delivery.OtpHash))
            return null;

        return await access.CheckAsync(delivery, ct) == DeliveryAccessDecision.Allowed ? delivery : null;
    }

    /// <summary>A filename for the archive, derived from the delivery rather than from a guess.</summary>
    public static string ArchiveName(Delivery delivery)
    {
        var stem = Path.GetFileNameWithoutExtension(delivery.FileName);
        if (string.IsNullOrWhiteSpace(stem))
            stem = delivery.ShareCode;

        var cleaned = new string(stem.Where(c => !char.IsControl(c) && c != '"' && c != '\\' && c != '/').ToArray());
        if (cleaned.Length > ArchiveNameMaxLength)
            cleaned = cleaned[..ArchiveNameMaxLength];

        return $"{cleaned}.zip";
    }

    /// <summary>De-duplicates entry names by suffixing, the way a desktop file manager would.</summary>
    public static string UniqueEntryName(string name, HashSet<string> used)
    {
        if (used.Add(name))
            return name;

        var directory = Path.GetDirectoryName(name)?.Replace('\\', '/');
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);

        for (var n = 2; ; n++)
        {
            var candidate = string.IsNullOrEmpty(directory)
                ? $"{stem} ({n}){extension}"
                : $"{directory}/{stem} ({n}){extension}";

            if (used.Add(candidate))
                return candidate;
        }
    }
}
