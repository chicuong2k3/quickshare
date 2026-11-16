using System.Text.Json;
using QuickShare.Extensions;
using QuickShare.Features.Workspaces.Shared;
using QuickShare.Infrastructure.Jobs;

namespace QuickShare.Features.Deliveries.Recurring;

public sealed record RecurringDto(
    Guid Id,
    string Name,
    Guid SourceDeliveryId,
    string SourceFileName,
    string Frequency,
    int HourUtc,
    DayOfWeek DayOfWeek,
    int DayOfMonth,
    string Recipients,
    bool Enabled,
    DateTime NextRunAt,
    DateTime? LastRunAt,
    int RunCount,
    string? LastError);

public sealed class RecurringRequest
{
    public string? Name { get; set; }
    public Guid SourceDeliveryId { get; set; }
    public string? Frequency { get; set; }
    public int HourUtc { get; set; }
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;
    public int DayOfMonth { get; set; } = 1;
    public string? Recipients { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>GET /api/recurring — the caller's standing send schedules (issue #176).</summary>
public sealed class ListRecurringEndpoint : EndpointWithoutRequest<IReadOnlyList<RecurringDto>>
{
    public override void Configure()
    {
        Get("/api/recurring");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s => s.Summary = "List recurring delivery schedules");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var schedules = await db.RecurringDeliveries
            .AsNoTracking()
            .Where(r => r.OwnerId == user.Id)
            .OrderBy(r => r.NextRunAt)
            .Select(r => new RecurringDto(
                r.Id,
                r.Name,
                r.SourceDeliveryId,
                db.Deliveries.Where(d => d.Id == r.SourceDeliveryId).Select(d => d.FileName).FirstOrDefault() ?? string.Empty,
                r.Frequency,
                r.HourUtc,
                r.DayOfWeek,
                r.DayOfMonth,
                r.Recipients,
                r.Enabled,
                r.NextRunAt,
                r.LastRunAt,
                r.RunCount,
                r.LastError))
            .ToListAsync(ct);

        await Send.OkAsync(schedules, ct);
    }
}

/// <summary>
/// POST /api/recurring — schedule a delivery to re-send on a cadence (issue #176).
///
/// Paid plans only, and the source must be a live delivery the caller owns: a schedule pointing
/// at somebody else's file would be a way to keep mailing content after losing access to it.
/// </summary>
public sealed class CreateRecurringEndpoint : Endpoint<RecurringRequest, RecurringDto>
{
    public override void Configure()
    {
        Post("/api/recurring");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Create a recurring delivery schedule";
            s.Description = "Pro and Studio. Each cycle copies the source file to a new delivery with a fresh share code and mails the recipient list.";
        });
    }

    public override async Task HandleAsync(RecurringRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        if (user.Plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
        {
            ThrowError(ErrorCodes.RequiresPaidPlan, StatusCodes.Status403Forbidden);
            return;
        }

        var name = (req.Name ?? string.Empty).Trim();
        var frequency = (req.Frequency ?? RecurrenceFrequency.Weekly).Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(name)
            || name.Length > RecurringDelivery.NameMaxLength
            || !RecurrenceFrequency.All.Contains(frequency)
            || req.HourUtc < RecurringDelivery.MinHour
            || req.HourUtc > RecurringDelivery.MaxHour)
        {
            ThrowError(ErrorCodes.RecurrenceInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var recipients = NormalizeRecipients(req.Recipients);
        if (recipients.Length == 0)
        {
            // A schedule with nobody to send to would fire forever into the void.
            ThrowError(ErrorCodes.RecurrenceRecipientsRequired, StatusCodes.Status400BadRequest);
            return;
        }

        var source = await db.Deliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == req.SourceDeliveryId
                && d.SenderId == user.Id
                && d.DeletedAt == null, ct);

        if (source is null || source.Status != DeliveryStatus.Active)
        {
            ThrowError(ErrorCodes.DeliveryNotActive, StatusCodes.Status400BadRequest);
            return;
        }

        var now = DateTime.UtcNow;
        var schedule = new RecurringDelivery
        {
            Id = Guid.CreateVersion7(),
            OwnerId = user.Id,
            WorkspaceId = source.WorkspaceId,
            SourceDeliveryId = source.Id,
            Name = name,
            Frequency = frequency,
            HourUtc = req.HourUtc,
            DayOfWeek = req.DayOfWeek,
            DayOfMonth = Math.Clamp(req.DayOfMonth, 1, 28),
            Recipients = recipients,
            Enabled = req.Enabled,
            CreatedAt = now,
        };
        schedule.NextRunAt = schedule.ComputeNextRun(now);

        db.RecurringDeliveries.Add(schedule);
        await db.SaveChangesAsync(ct);

        await AuditAsync(db, Resolve<WorkspaceService>(), schedule, HttpContext, "created", ct);

        await Send.OkAsync(Map(schedule, source.FileName), ct);
    }

    /// <summary>Lowercased, de-duplicated, capped — the same shape the mail loop will read back.</summary>
    internal static string NormalizeRecipients(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var addresses = raw
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => a.ToLowerInvariant())
            .Where(a => a.Contains('@'))
            .Distinct()
            .ToList();

        var joined = string.Join(',', addresses);
        return joined.Length > RecurringDelivery.RecipientsMaxLength
            ? joined[..joined.LastIndexOf(',', RecurringDelivery.RecipientsMaxLength - 1)]
            : joined;
    }

    internal static RecurringDto Map(RecurringDelivery schedule, string fileName) => new(
        schedule.Id,
        schedule.Name,
        schedule.SourceDeliveryId,
        fileName,
        schedule.Frequency,
        schedule.HourUtc,
        schedule.DayOfWeek,
        schedule.DayOfMonth,
        schedule.Recipients,
        schedule.Enabled,
        schedule.NextRunAt,
        schedule.LastRunAt,
        schedule.RunCount,
        schedule.LastError);

    internal static async Task AuditAsync(
        AppDbContext db, WorkspaceService workspaces, RecurringDelivery schedule,
        HttpContext http, string action, CancellationToken ct)
    {
        if (schedule.WorkspaceId is not { } workspaceId)
            return;

        await workspaces.AuditAsync(workspaceId, schedule.OwnerId,
            SecurityEventType.RecurringDeliveryChanged, http, ct,
            JsonSerializer.Serialize(new { schedule.Name, schedule.Frequency, action }));
    }
}

public sealed class UpdateRecurringRequest
{
    public bool? Enabled { get; set; }
    public string? Recipients { get; set; }
    /// <summary>Push the next run one cycle forward without changing the cadence.</summary>
    public bool SkipNext { get; set; }
}

/// <summary>
/// PATCH /api/recurring/{Id} — pause, resume, change recipients, or skip the next run (#176).
///
/// Skip is separate from pause because they answer different questions: "not this week" is a
/// one-off, "not for now" is indefinite, and collapsing them would make the owner choose
/// between losing the schedule and sending something they did not want to send.
/// </summary>
public sealed class UpdateRecurringEndpoint : Endpoint<UpdateRecurringRequest, RecurringDto>
{
    public override void Configure()
    {
        Patch("/api/recurring/{Id}");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s => s.Summary = "Pause, resume, retarget or skip a recurring schedule");
    }

    public override async Task HandleAsync(UpdateRecurringRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var schedule = await db.RecurringDeliveries
            .FirstOrDefaultAsync(r => r.Id == Route<Guid>("Id") && r.OwnerId == user.Id, ct);

        if (schedule is null)
        {
            ThrowError(ErrorCodes.RecurrenceNotFound, StatusCodes.Status404NotFound);
            return;
        }

        if (req.Enabled is { } enabled)
        {
            // Resuming re-bases the schedule on now, so a paused-for-a-month weekly send does
            // not fire the instant it is switched back on.
            if (enabled && !schedule.Enabled)
                schedule.NextRunAt = schedule.ComputeNextRun(DateTime.UtcNow);

            schedule.Enabled = enabled;
            schedule.LastError = null;
        }

        if (req.Recipients is not null)
        {
            var recipients = CreateRecurringEndpoint.NormalizeRecipients(req.Recipients);
            if (recipients.Length == 0)
            {
                ThrowError(ErrorCodes.RecurrenceRecipientsRequired, StatusCodes.Status400BadRequest);
                return;
            }

            schedule.Recipients = recipients;
        }

        if (req.SkipNext)
            schedule.NextRunAt = schedule.ComputeNextRun(schedule.NextRunAt);

        schedule.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await CreateRecurringEndpoint.AuditAsync(
            db, Resolve<WorkspaceService>(), schedule, HttpContext, "updated", ct);

        var fileName = await db.Deliveries
            .Where(d => d.Id == schedule.SourceDeliveryId)
            .Select(d => d.FileName)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        await Send.OkAsync(CreateRecurringEndpoint.Map(schedule, fileName), ct);
    }
}

/// <summary>DELETE /api/recurring/{Id} — stop a schedule (issue #176).</summary>
public sealed class DeleteRecurringEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/recurring/{Id}");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Delete a recurring schedule";
            s.Description = "Deliveries already generated by it are untouched — they are real shares recipients may still be using.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var schedule = await db.RecurringDeliveries
            .FirstOrDefaultAsync(r => r.Id == Route<Guid>("Id") && r.OwnerId == user.Id, ct);

        if (schedule is null)
        {
            ThrowError(ErrorCodes.RecurrenceNotFound, StatusCodes.Status404NotFound);
            return;
        }

        db.RecurringDeliveries.Remove(schedule);
        await db.SaveChangesAsync(ct);

        await CreateRecurringEndpoint.AuditAsync(
            db, Resolve<WorkspaceService>(), schedule, HttpContext, "deleted", ct);

        await Send.NoContentAsync(ct);
    }
}

/// <summary>
/// POST /api/recurring/{Id}/run — fire a cycle now (issue #176).
///
/// Same method as the hourly tick, so a manual run proves something about the scheduled one.
/// It also advances the schedule, because a "test" that then fires again an hour later is a
/// duplicate the recipients have to reconcile.
/// </summary>
public sealed class RunRecurringNowEndpoint : EndpointWithoutRequest<RecurringDto>
{
    public override void Configure()
    {
        Post("/api/recurring/{Id}/run");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s => s.Summary = "Run a recurring schedule now");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var schedule = await db.RecurringDeliveries
            .FirstOrDefaultAsync(r => r.Id == Route<Guid>("Id") && r.OwnerId == user.Id, ct);

        if (schedule is null)
        {
            ThrowError(ErrorCodes.RecurrenceNotFound, StatusCodes.Status404NotFound);
            return;
        }

        await Resolve<RecurringDeliveryJob>().RunOneAsync(db, schedule, DateTime.UtcNow, ct);
        await db.SaveChangesAsync(ct);

        var fileName = await db.Deliveries
            .Where(d => d.Id == schedule.SourceDeliveryId)
            .Select(d => d.FileName)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        await Send.OkAsync(CreateRecurringEndpoint.Map(schedule, fileName), ct);
    }
}
