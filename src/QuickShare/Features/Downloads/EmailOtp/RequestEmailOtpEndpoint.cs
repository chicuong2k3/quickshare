namespace QuickShare.Features.Downloads.EmailOtp;

public sealed class RequestEmailOtpEndpoint : Endpoint<RequestEmailOtpRequest, RequestEmailOtpResponse>
{
    public override void Configure()
    {
        Post("/api/deliveries/{Code}/request-email-otp");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Downloads));
        Summary(s =>
        {
            s.Summary = "Email a single-use code to unlock an email-OTP-gated delivery";
            s.Description = "Code is BCrypt-hashed, TTL 15 minutes, 60s resend cooldown.";
            s.Responses[200] = "Code emailed.";
            s.Responses[400] = "Gate disabled, expired, or invalid email.";
            s.Responses[404] = "Delivery not found.";
            s.Responses[429] = "Resend cooldown not elapsed.";
        });
    }

    public override async Task HandleAsync(RequestEmailOtpRequest req, CancellationToken ct)
    {
        var code = Route<string>("Code") ?? string.Empty;
        var result = await Resolve<IMediator>().Send<LightResults.Result<RequestEmailOtpResponse>>(
            new RequestEmailOtpCommand(code, req.Email), ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound => 404,
                ErrorCodes.EmailOtpCooldown => 429,
                _ => 400
            });
    }
}

public class RequestEmailOtpRequest
{
    public string Email { get; set; } = string.Empty;
}
