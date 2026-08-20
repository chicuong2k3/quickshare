namespace QuickShare.Features.Users.Retention;

public sealed class RetentionPreviewRequest
{
    /// <summary>Proposed retention window in days; null/absent means "no policy" and affects nothing.</summary>
    public int? Days { get; set; }
}

/// <summary>GET /api/account/retention/preview — dry-run a retention window (issue #161).</summary>
public sealed class RetentionPreviewEndpoint : Endpoint<RetentionPreviewRequest>
{
    public override void Configure()
    {
        Get("/api/account/retention/preview");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "Preview what a retention window would shorten";
            s.Description = "Counts the caller's own live shares whose expiry sits beyond the proposed window, with the affected expiry range and up to 20 sample rows. Read-only — nothing is clamped. Legally held rows are excluded because the sweep never clamps them.";
        });
    }

    public override async Task HandleAsync(RetentionPreviewRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<LightResults.Result<RetentionPreviewResponse>>(
            new RetentionPreviewQuery(req.Days), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, StatusCodes.Status400BadRequest);
    }
}
