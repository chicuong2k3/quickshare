using System.Text.Json;
using LightResults;
using Microsoft.EntityFrameworkCore;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Features.Users.AccountDeletion;

/// <summary>JSON export of the signed-in user's profile + deliveries metadata (issue #59).</summary>
public sealed class ExportMyDataEndpoint : EndpointWithoutRequest
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    public override void Configure()
    {
        Get("/api/me/export");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "Export the signed-in user's data as JSON";
            s.Description = "Profile + deliveries metadata (no object bytes).";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var deliveries = await db.Deliveries
            .AsNoTracking()
            .Where(d => d.SenderId == user.Id)
            .Select(d => new
            {
                d.Id,
                d.FileName,
                d.ShareCode,
                d.Alias,
                d.FileSize,
                d.ContentType,
                d.Status,
                d.CreatedAt,
                d.ExpiresAt,
                d.DownloadCount
            })
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(ct);

        // Security audit trail (issue #126) — exported as part of GDPR data export.
        var securityEvents = (await db.SecurityEvents
                .AsNoTracking()
                .Where(e => e.UserId == user.Id)
                .OrderBy(e => e.CreatedAt)
                .Select(e => new { e.EventType, e.UserAgentLabel, e.MetadataJson, e.CreatedAt })
                .ToListAsync(ct))
            .Select(e => new SecurityEventExportRow(e.EventType.ToString(), e.UserAgentLabel, e.MetadataJson, e.CreatedAt))
            .ToList();

        // Recipient contacts (issue #316) — recent recipients + address book, part of GDPR export.
        var recipientContacts = await db.RecipientContacts
            .AsNoTracking()
            .Where(c => c.UserId == user.Id)
            .OrderBy(c => c.Email)
            .Select(c => new { c.Email, c.Name, c.LastUsedAt, c.UseCount })
            .ToListAsync(ct);

        var payload = new
        {
            ExportedAt = DateTime.UtcNow,
            Profile = new
            {
                user.Id,
                user.Name,
                user.Email,
                user.Plan,
                user.AvatarUrl,
                user.CreatedAt,
                user.ReferralCode,
                user.BonusQuotaBytes
            },
            Deliveries = deliveries,
            SecurityEvents = securityEvents,
            RecipientContacts = recipientContacts
        };

        var json = JsonSerializer.Serialize(payload, IndentedOptions);
        var filename = $"quickshare-export-{DateTime.UtcNow:yyyyMMdd}.json";
        HttpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";
        await Send.StringAsync(json, StatusCodes.Status200OK, "application/json; charset=utf-8", ct);
    }
}

/// <summary>Security audit event row included in the GDPR data export (issue #126).</summary>
public record SecurityEventExportRow(string Type, string UserAgentLabel, string? MetadataJson, DateTime CreatedAt);

public record DeleteAccountCommand(string ConfirmEmail) : IRequest<Result>;

/// <summary>
/// Permanently deletes the signed-in account and everything it owns (issue #59):
/// R2 objects, deliveries (cascade to tracking/OTP/abuse rows), referral events,
/// then the user (cascade to logins/usage/devices/payments/subscriptions).
/// </summary>
public sealed class DeleteAccountHandler(AppDbContext db, IHttpContextAccessor http, IStorageProvider storage)
    : IRequestHandler<DeleteAccountCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteAccountCommand cmd, CancellationToken cancellationToken)
    {
        var cached = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var user = await db.Users.FirstAsync(u => u.Id == cached.Id, cancellationToken);

        if (!string.Equals(cmd.ConfirmEmail?.Trim(), user.Email, StringComparison.OrdinalIgnoreCase))
            return Result.Failure(new LightResults.Error(ErrorCodes.AccountDeleteConfirmMismatch));

        var deliveries = await db.Deliveries
            .Where(d => d.SenderId == user.Id)
            .ToListAsync(cancellationToken);

        // Legal hold (issue #266): account deletion is all-or-nothing, so a single held
        // delivery refuses the whole operation. A partial delete would leave the account
        // gone and the frozen evidence orphaned with no owner able to lift the hold.
        if (deliveries.Any(d => d.LegalHoldAt is not null))
            return Result.Failure(new LightResults.Error(ErrorCodes.LegalHoldActive));

        // Workspace ownership (issues #106/#252): deleting the owner would take the whole
        // team's workspace with it. Refuse and point at the transfer flow — the successor
        // has to accept before the account can go.
        var ownsWorkspace = await db.Workspaces
            .AnyAsync(w => w.OwnerId == user.Id, cancellationToken);
        if (ownsWorkspace)
            return Result.Failure(new LightResults.Error(ErrorCodes.WorkspaceTransferRequired));

        foreach (var d in deliveries)
        {
            if (!string.IsNullOrEmpty(d.StoragePath))
            {
                try
                {
                    await storage.DeleteObjectsAsync(d.StoragePath, cancellationToken);
                }
                catch
                {
                    // Best-effort object cleanup — never block account deletion on a storage hiccup.
                }
            }
        }

        db.Deliveries.RemoveRange(deliveries);

        // ReferralEvent has plain Guid columns (no FK) — clean up explicitly.
        await db.ReferralEvents
            .Where(r => r.ReferrerUserId == user.Id || r.ReferredUserId == user.Id)
            .ExecuteDeleteAsync(cancellationToken);

        db.Users.Remove(user);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed class DeleteAccountEndpoint : Endpoint<DeleteAccountRequest>
{
    public override void Configure()
    {
        Post("/api/me/delete");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "Permanently delete the signed-in account and all its data";
            s.Description = "Requires the account email as confirmation. Destructive and irreversible.";
        });
    }

    public override async Task HandleAsync(DeleteAccountRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<LightResults.Result>(
            new DeleteAccountCommand(req.ConfirmEmail ?? string.Empty), ct);

        if (result.IsSuccess())
            await Send.NoContentAsync(ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message is ErrorCodes.LegalHoldActive or ErrorCodes.WorkspaceTransferRequired
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest);
    }
}

public class DeleteAccountRequest
{
    public string? ConfirmEmail { get; set; }
}
