namespace QuickShare.Features.Deliveries.Otp;

public sealed class VerifyOtpEndpoint : Endpoint<VerifyOtpRequest>
{
    public override void Configure()
    {
        Post("/api/deliveries/{ShareCode}/verify-otp");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Deliveries));
    }

    public override async Task HandleAsync(VerifyOtpRequest req, CancellationToken ct)
    {
        var shareCode = Route<string>("ShareCode")!;
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<VerifyOtpResponse>>(
            new VerifyOtpCommand(shareCode, req.Otp), ct);

        if (result.IsSuccess(out var v))
            await Send.OkAsync(v, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, 400);
    }
}

public class VerifyOtpRequest
{
    public string Otp { get; set; } = string.Empty;
}
