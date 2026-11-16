using System.Text;
using LightResults;

namespace QuickShare.Features.Deliveries.LegalHold;

public sealed class PlaceLegalHoldRequest
{
    public string? Reason { get; set; }
}

/// <summary>POST /api/deliveries/{Id}/legal-hold — freeze a delivery against deletion (issue #266).</summary>
public sealed class PlaceLegalHoldEndpoint : Endpoint<PlaceLegalHoldRequest, LegalHoldResponse>
{
    public override void Configure()
    {
        Post("/api/deliveries/{Id}/legal-hold");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Place a legal hold on a delivery";
            s.Description = "Sender only. Exempts the delivery from retention, trash, purge, expiry cleanup and account deletion until the hold is lifted by hand. A case reference is required.";
        });
    }

    public override async Task HandleAsync(PlaceLegalHoldRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("Id");

        var result = await Resolve<IMediator>().Send<LightResults.Result<LegalHoldResponse>>(
            new PlaceLegalHoldCommand(id, req.Reason), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound => StatusCodes.Status404NotFound,
                ErrorCodes.LegalHoldActive => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            });
    }
}

/// <summary>POST /api/deliveries/{Id}/legal-hold/lift — release a hold (issue #266).</summary>
public sealed class LiftLegalHoldEndpoint : EndpointWithoutRequest<LegalHoldResponse>
{
    public override void Configure()
    {
        Post("/api/deliveries/{Id}/legal-hold/lift");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Lift a legal hold";
            s.Description = "Sender only. Manual release; holds never expire on their own. Both transitions are recorded on the account security timeline.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("Id");

        var result = await Resolve<IMediator>().Send<LightResults.Result<LegalHoldResponse>>(
            new LiftLegalHoldCommand(id), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound => StatusCodes.Status404NotFound,
                ErrorCodes.LegalHoldNotActive => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            });
    }
}

/// <summary>
/// GET /api/deliveries/legal-hold/export — the hold report counsel asks for (issue #266).
/// One row per held delivery, so a legal department can evidence exactly what is frozen.
/// </summary>
public sealed class ExportLegalHoldReportEndpoint : EndpointWithoutRequest
{
    private const string CsvHeader = "share_code,file_name,file_size_bytes,held_at_utc,case_reference,status";

    public override void Configure()
    {
        Get("/api/deliveries/legal-hold/export");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Export the caller's legal-hold report as CSV";
            s.Description = "Sender only. One row per held delivery, for counsel.";
        });
        DontAutoSendResponse();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var rows = await db.Deliveries
            .AsNoTracking()
            .Where(d => d.SenderId == user.Id && d.LegalHoldAt != null)
            .OrderByDescending(d => d.LegalHoldAt)
            .Select(d => new
            {
                d.ShareCode,
                d.FileName,
                d.FileSize,
                d.LegalHoldAt,
                d.LegalHoldReason,
                d.Status,
            })
            .ToListAsync(ct);

        var csv = new StringBuilder();
        csv.AppendLine(CsvHeader);

        foreach (var row in rows)
        {
            csv.Append(Csv(row.ShareCode));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(Csv(row.FileName));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(row.FileSize.ToString(CultureInfo.InvariantCulture));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(row.LegalHoldAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(Csv(row.LegalHoldReason ?? string.Empty));
            csv.Append(DeliveryAccessCsv.Separator);
            csv.Append(Csv(row.Status));
            csv.AppendLine();
        }

        var filename = $"quickshare-legal-hold-{DateTime.UtcNow:yyyyMMdd}.csv";
        HttpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";
        await Send.StringAsync(csv.ToString(), StatusCodes.Status200OK, "text/csv; charset=utf-8", ct);
    }

    /// <summary>A case reference is free text — always quote, and double any embedded quote.</summary>
    private static string Csv(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

/// <summary>CSV wire format for delivery exports.</summary>
public static class DeliveryAccessCsv
{
    public const char Separator = ',';
}
