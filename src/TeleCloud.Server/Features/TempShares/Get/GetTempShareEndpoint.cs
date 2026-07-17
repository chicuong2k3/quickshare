namespace TeleCloud.Server.Features.TempShares.Get;

public sealed class GetTempShareEndpoint : Endpoint<GetTempShareQuery, TempShareInfoResponse>
{
    public override void Configure()
    {
        Get("/api/temp/{id}");
        AllowAnonymous();
        Description(b => b.WithTags("TempShares"));
        Summary(s =>
        {
            s.Summary = "Get temp share metadata";
            s.Description = "Returns metadata for a temp share. If the row exists but is past expiry, returns 200 with expired:true (so the page can render the Expired state with filename). Returns 404 only when the id does not exist.";
            s.Params["id"] = "Unique identifier (Guid) of the temp share.";
            s.Responses[200] = "Share metadata with an expired flag.";
            s.Responses[404] = "No share with the given id exists.";
        });
    }

    public override async Task HandleAsync(GetTempShareQuery req, CancellationToken ct)
    {
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<TempShareInfoResponse>>(req, ct);
        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else
            await Send.NotFoundAsync(ct);
    }
}
