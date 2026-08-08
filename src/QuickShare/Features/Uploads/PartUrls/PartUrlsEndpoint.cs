namespace QuickShare.Features.Uploads.PartUrls;

public sealed class PartUrlsEndpoint : Endpoint<PartUrlsRequest, PartUrlsResponse>
{
    public override void Configure()
    {
        Post("/api/uploads/part-urls");
        Validator<PartUrlsValidator>();
        Description(b => b.WithTags("Uploads"));
        Summary(s =>
        {
            s.Summary = "Regenerate presigned part URLs for an in-progress upload";
            s.Description = "Returns fresh presigned PUT URLs for the given parts so the client can retry or resume after a URL expires.";
            s.Responses[200] = "Fresh presigned part URLs.";
            s.Responses[404] = "No matching in-progress upload session.";
            s.Responses[502] = "R2 upstream failure.";
        });
    }

    public override async Task HandleAsync(PartUrlsRequest req, CancellationToken ct)
    {
        var cmd = new PartUrlsCommand(req.DeliveryId, req.UploadId, req.PartNumbers);
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<PartUrlsResponse>>(cmd, ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message.Contains("R2") ? 502 : 404);
    }
}

public class PartUrlsRequest
{
    public Guid DeliveryId { get; set; }
    public string UploadId { get; set; } = "";
    public IReadOnlyList<int> PartNumbers { get; set; } = [];
}
