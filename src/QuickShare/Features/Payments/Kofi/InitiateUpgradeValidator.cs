namespace QuickShare.Features.Payments.Kofi;

public sealed class InitiateUpgradeValidator : Validator<InitiateUpgradeRequest>
{
    public InitiateUpgradeValidator()
    {
        RuleFor(x => x.Plan)
            .NotEmpty()
            .WithMessage("Plan is required.")
            .Must(p => p is "pro" or "studio")
            .WithMessage("Invalid plan. Must be 'pro' or 'studio'.");

        RuleFor(x => x.Billing)
            .NotEmpty()
            .WithMessage("Billing is required.")
            .Must(b => string.Equals(b, "monthly", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(b, "yearly", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Billing must be 'monthly' or 'yearly'.");
    }
}
