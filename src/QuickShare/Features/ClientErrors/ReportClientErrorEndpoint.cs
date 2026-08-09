namespace QuickShare.Features.ClientErrors;

/// <summary>Accepts browser-side JavaScript errors so they can be written to the server-side log (Serilog → Seq).</summary>
public sealed class ReportClientErrorEndpoint(ILogger<ReportClientErrorEndpoint> logger) : Endpoint<ReportClientErrorRequest>
{
    public override void Configure()
    {
        Post("/api/client-errors");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.ClientErrors));
        Summary(s =>
        {
            s.Summary = "Report a browser-side JavaScript error";
            s.Description = "Called by the client-side error handler; writes the error to the server log.";
            s.Responses[204] = "Error accepted.";
        });
    }

    public override async Task HandleAsync(ReportClientErrorRequest req, CancellationToken ct)
    {
        var userAgent = HttpContext.Request.Headers.UserAgent.FirstOrDefault() ?? "unknown";

        logger.LogError(
            "Client JS error: {ClientErrorMessage} at {ClientErrorSource}:{ClientErrorLine}:{ClientErrorColumn} | page {ClientErrorHref} | UA {ClientErrorUserAgent}{ClientErrorStack}",
            req.Message,
            req.Source,
            req.Line,
            req.Column,
            req.Href,
            userAgent,
            string.IsNullOrWhiteSpace(req.Stack) ? string.Empty : "\n" + req.Stack);

        await Send.NoContentAsync(ct);
    }
}

public class ReportClientErrorRequest
{
    public string Message { get; set; } = string.Empty;
    public string Stack { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
    public string Href { get; set; } = string.Empty;
}
