using QuickShare.Extensions;

namespace QuickShare.Features.Uploads.Presets;

public sealed record PresetDto(
    Guid Id,
    string Name,
    double? ExpiryHours,
    string? Password,
    bool BurnAfterDownload,
    bool PrivacyMode,
    string? Recipients,
    DateTime CreatedAt);

public sealed class CreatePresetRequest
{
    public string? Name { get; set; }
    public double? ExpiryHours { get; set; }
    public string? Password { get; set; }
    public bool BurnAfterDownload { get; set; }
    public bool PrivacyMode { get; set; }
    public string? Recipients { get; set; }
}

/// <summary>GET /api/upload-presets — the caller's saved send configurations (issue #100).</summary>
public sealed class ListPresetsEndpoint : EndpointWithoutRequest<IReadOnlyList<PresetDto>>
{
    public override void Configure()
    {
        Get("/api/upload-presets");
        Description(b => b.WithTags(ApiTags.Uploads));
        Summary(s => s.Summary = "List the caller's upload presets");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var presets = await db.UploadPresets
            .AsNoTracking()
            .Where(p => p.OwnerId == user.Id)
            .OrderBy(p => p.Name)
            .Select(p => ToDto(p))
            .ToListAsync(ct);

        await Send.OkAsync(presets, ct);
    }

    internal static PresetDto ToDto(UploadPreset p) => new(
        p.Id, p.Name, p.ExpiryHours, p.Password,
        p.BurnAfterDownload, p.PrivacyMode, p.Recipients, p.CreatedAt);
}

/// <summary>
/// POST /api/upload-presets — save a send configuration (issue #100).
///
/// Pro/Studio only: presets are a power-user retention feature, not free-tier infrastructure.
/// Capped at <see cref="UploadPreset.MaxPresetsPerUser"/> so the endpoint is not a storage sink.
/// </summary>
public sealed class CreatePresetEndpoint : Endpoint<CreatePresetRequest, PresetDto>
{
    public override void Configure()
    {
        Post("/api/upload-presets");
        Description(b => b.WithTags(ApiTags.Uploads));
        Summary(s =>
        {
            s.Summary = "Create an upload preset";
            s.Description = "Pro and Studio only. Max 10 per user.";
        });
    }

    public override async Task HandleAsync(CreatePresetRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        if (user.Plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
        {
            ThrowError(ErrorCodes.RequiresPaidPlan, StatusCodes.Status403Forbidden);
            return;
        }

        var name = (req.Name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name) || name.Length > UploadPreset.NameMaxLength)
        {
            ThrowError(ErrorCodes.PresetNameInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var count = await db.UploadPresets.CountAsync(p => p.OwnerId == user.Id, ct);
        if (count >= UploadPreset.MaxPresetsPerUser)
        {
            ThrowError(ErrorCodes.PresetLimitReached, StatusCodes.Status400BadRequest);
            return;
        }

        var password = string.IsNullOrWhiteSpace(req.Password) ? null
            : req.Password.Trim().Length > UploadPreset.PasswordMaxLength ? null
            : req.Password.Trim();

        var recipients = NormalizeRecipients(req.Recipients);

        var preset = new UploadPreset
        {
            Id = Guid.CreateVersion7(),
            OwnerId = user.Id,
            Name = name,
            ExpiryHours = req.ExpiryHours,
            Password = password,
            BurnAfterDownload = req.BurnAfterDownload,
            PrivacyMode = req.PrivacyMode,
            Recipients = string.IsNullOrEmpty(recipients) ? null : recipients,
            CreatedAt = DateTime.UtcNow,
        };

        db.UploadPresets.Add(preset);
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(ListPresetsEndpoint.ToDto(preset), ct);
    }

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
        return joined.Length > UploadPreset.RecipientsMaxLength
            ? joined[..joined.LastIndexOf(',', UploadPreset.RecipientsMaxLength - 1)]
            : joined;
    }
}

/// <summary>DELETE /api/upload-presets/{id} — remove a saved preset (issue #100).</summary>
public sealed class DeletePresetEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/upload-presets/{id}");
        Description(b => b.WithTags(ApiTags.Uploads));
        Summary(s => s.Summary = "Delete an upload preset");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var preset = await db.UploadPresets
            .FirstOrDefaultAsync(p => p.Id == Route<Guid>("id") && p.OwnerId == user.Id, ct);

        if (preset is null)
        {
            ThrowError(ErrorCodes.PresetNotFound, StatusCodes.Status404NotFound);
            return;
        }

        db.UploadPresets.Remove(preset);
        await db.SaveChangesAsync(ct);

        await Send.NoContentAsync(ct);
    }
}
