using QuickShare.Extensions;

namespace QuickShare.Features.Deliveries.Recipients;

/// <summary>What one addressed recipient did with their link (issue #37).</summary>
public sealed record RecipientActivityDto(string Email, DateTime SentAt, DateTime? OpenedAt, DateTime? DownloadedAt);

/// <summary>How often one file inside a bundle was taken (issue #154).</summary>
public sealed record FileActivityDto(string FileName, long FileSize, int DownloadCount);

public sealed record DeliveryActivityDto(
    IReadOnlyList<RecipientActivityDto> Recipients,
    IReadOnlyList<FileActivityDto> Files,
    bool PrivacyMode);

/// <summary>
/// GET /api/deliveries/{Id}/activity — who took it, and which parts (issues #37, #154).
///
/// The sender's view of their own share, and nobody else's: a recipient list is a list of people
/// somebody chose to mail, and the delivery's own recipient page never sees any of it.
///
/// A privacy-mode share answers with the flag set and both lists empty rather than with a 404 —
/// the sender should be told *why* there is nothing here, since they are the one who asked for
/// nothing to be recorded.
/// </summary>
public sealed class DeliveryActivityEndpoint : EndpointWithoutRequest<DeliveryActivityDto>
{
    public override void Configure()
    {
        Get("/api/deliveries/{Id}/activity");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Per-recipient and per-file activity for a delivery";
            s.Description = "Sender only (issues #37, #154). Empty in privacy mode, which is reported rather than hidden.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);
        var id = Route<Guid>("Id");

        var delivery = await db.Deliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && d.SenderId == user.Id, ct);

        if (delivery is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (delivery.PrivacyMode)
        {
            await Send.OkAsync(new DeliveryActivityDto([], [], PrivacyMode: true), ct);
            return;
        }

        var recipients = await db.DeliveryRecipients
            .AsNoTracking()
            .Where(r => r.DeliveryId == delivery.Id)
            .OrderBy(r => r.Email)
            .Select(r => new RecipientActivityDto(r.Email, r.SentAt, r.OpenedAt, r.DownloadedAt))
            .ToListAsync(ct);

        // Only worth listing for a bundle: for a single-file delivery the per-file count and the
        // delivery's own count are the same number said twice.
        var files = delivery.FileCount > 1
            ? await db.DeliveryFiles
                .AsNoTracking()
                .Where(f => f.DeliveryId == delivery.Id)
                .OrderBy(f => f.SortOrder)
                .Select(f => new FileActivityDto(f.FileName, f.FileSize, f.DownloadCount))
                .ToListAsync(ct)
            : [];

        await Send.OkAsync(new DeliveryActivityDto(recipients, files, PrivacyMode: false), ct);
    }
}
