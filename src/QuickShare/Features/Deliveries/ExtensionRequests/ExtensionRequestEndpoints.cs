namespace QuickShare.Features.Deliveries.ExtensionRequests;

public sealed class RequestExtensionEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/deliveries/{Code}/extension-request");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Ask the sender to extend an expired delivery";
            s.Description = "Anonymous, one open request per recipient per delivery. Only when the sender enabled requests.";
            s.Responses[200] = "Request recorded (idempotent).";
            s.Responses[400] = "Requests disabled, delivery not expired, or not found.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var code = Route<string>("Code") ?? string.Empty;
        var result = await Resolve<IMediator>().Send<LightResults.Result<RequestExtensionResponse>>(
            new RequestExtensionCommand(code), ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound or ErrorCodes.DeliveryNotActive => 404,
                _ => 400
            });
    }
}

public sealed class ListExtensionRequestsEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/deliveries/{Id}/extension-requests");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s => s.Summary = "List extension requests for a delivery (sender)");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMediator>().Send<LightResults.Result<IReadOnlyList<ExtensionRequestItem>>>(
            new ListExtensionRequestsQuery(id), ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, 404);
    }
}

public sealed class ApproveExtensionRequestEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/extension-requests/{Id}/approve");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s => s.Summary = "Approve an extension request and extend the delivery");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMediator>().Send<LightResults.Result<ApproveExtensionRequestResponse>>(
            new ApproveExtensionRequestCommand(id), ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.ExtensionRequestNotFound or ErrorCodes.NotFound => 404,
                _ => 400
            });
    }
}

public sealed class RejectExtensionRequestEndpoint : Endpoint<RejectExtensionRequest, RejectExtensionResponse>
{
    public override void Configure()
    {
        Post("/api/extension-requests/{Id}/reject");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s => s.Summary = "Reject an extension request");
    }

    public override async Task HandleAsync(RejectExtensionRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMediator>().Send<LightResults.Result>(
            new RejectExtensionRequestCommand(id, req.Message), ct);

        if (result.IsSuccess())
            await Send.OkAsync(new RejectExtensionResponse(true), ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.ExtensionRequestNotFound or ErrorCodes.NotFound => 404,
                _ => 400
            });
    }
}

public class RejectExtensionRequest
{
    public string? Message { get; set; }
}

public sealed record RejectExtensionResponse(bool Rejected);
