namespace QuickShare.Server.Features.Reports;

public sealed class ReportDeliveryEndpoint : Endpoint<ReportDeliveryRequest>
{
    public override void Configure()
    {
        Post("/api/deliveries/{ShareCode}/report");
        AllowAnonymous();
        Description(b => b.WithTags("Reports"));
    }

    public override async Task HandleAsync(ReportDeliveryRequest req, CancellationToken ct)
    {
        var shareCode = Route<string>("ShareCode")!;
        var cmd = new ReportDeliveryCommand(shareCode, req.Reason, req.Message);
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<string>>(cmd, ct);
        if (result.IsSuccess(out var msg)) await Send.OkAsync(new { message = msg }, ct);
        else ThrowError("not_found", 404);
    }
}

public class ReportDeliveryRequest { public string Reason { get; set; } = ""; public string? Message { get; set; } }
