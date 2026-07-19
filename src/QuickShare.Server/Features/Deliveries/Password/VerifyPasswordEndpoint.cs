namespace QuickShare.Server.Features.Deliveries.Password;

public sealed class VerifyPasswordEndpoint : Endpoint<VerifyPasswordRequest>
{
    public override void Configure()
    {
        Post("/api/deliveries/{ShareCode}/verify");
        AllowAnonymous();
        Description(b => b.WithTags("Deliveries"));
    }

    public override async Task HandleAsync(VerifyPasswordRequest req, CancellationToken ct)
    {
        var shareCode = Route<string>("ShareCode")!;
        var cmd = new VerifyPasswordCommand(shareCode, req.Password);
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<VerifyPasswordResponse>>(cmd, ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
        {
            var status = error.Message.Contains("Too many") ? 429 : 401;
            ThrowError(error.Message, status);
        }
    }
}

public class VerifyPasswordRequest { public string Password { get; set; } = ""; }
