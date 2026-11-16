namespace QuickShare.Features.Analytics.Track;

public sealed class TrackEventRequest
{
    public string Event { get; set; } = string.Empty;
}

/// <summary>
/// Anonymous client-beacon endpoint (issue #24). Fire-and-forget tracking of
/// client-side events only (visitor landing, ad interstitial view) from pages
/// that render client JS. Server-side events are recorded in their handlers.
/// No auth, no PII — the visitor cookie is created here on first contact.
/// </summary>
public sealed class TrackEventEndpoint : Endpoint<TrackEventRequest>
{
    public override void Configure()
    {
        Post(AnalyticsConstants.TrackRoute);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Analytics));
        Summary(s =>
        {
            s.Summary = "Track an anonymous client-side analytics event";
            s.Description = "Accepts only allowlisted client-fireable event names. Sets the qs_visitor cookie when absent.";
        });
    }

    public override async Task HandleAsync(TrackEventRequest req, CancellationToken ct)
    {
        if (!AnalyticsEvents.ClientFireable.Contains(req.Event))
        {
            ThrowError($"Unknown analytics event '{req.Event}'", StatusCodes.Status400BadRequest);
            return;
        }

        var analytics = Resolve<AnalyticsService>();
        await analytics.RecordAsync(req.Event, ct: ct);
        await Send.NoContentAsync(ct);
    }
}
