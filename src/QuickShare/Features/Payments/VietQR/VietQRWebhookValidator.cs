namespace QuickShare.Features.Payments.VietQR;

public sealed class VietQRWebhookValidator : Validator<VietQRWebhookRequest>
{
    public VietQRWebhookValidator()
    {
        RuleFor(x => x.ShareCode)
            .NotEmpty()
            .WithMessage("Share code is required.");
    }
}
