namespace QuickShare.Features.Downloads.Preview;

public sealed class OfficePreviewEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/downloads/{Code}/office-preview");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Downloads));
        Summary(s =>
        {
            s.Summary = "Extracted text preview for an office document (docx/xlsx/pptx)";
            s.Description = "Server extracts and HTML-escapes document text; no macros are executed.";
            s.Responses[200] = "Extracted HTML plus the detected format.";
            s.Responses[400] = "Not an office document, gated, or too large.";
            s.Responses[404] = "Delivery not found.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var code = Route<string>("Code") ?? string.Empty;
        var result = await Resolve<IMediator>().Send<LightResults.Result<OfficePreviewResponse>>(
            new OfficePreviewQuery(code), ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound => 404,
                _ => 400
            });
    }
}
