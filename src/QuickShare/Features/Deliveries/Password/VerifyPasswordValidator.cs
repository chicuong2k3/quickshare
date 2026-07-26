namespace QuickShare.Features.Deliveries.Password;

public sealed class VerifyPasswordValidator : Validator<VerifyPasswordRequest>
{
    public VerifyPasswordValidator()
    {
        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("Password is required.");
    }
}
