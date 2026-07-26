namespace QuickShare.Features.Reports;

public sealed class ReportDeliveryValidator : Validator<ReportDeliveryRequest>
{
    public ReportDeliveryValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("Reason is required.");
    }
}
