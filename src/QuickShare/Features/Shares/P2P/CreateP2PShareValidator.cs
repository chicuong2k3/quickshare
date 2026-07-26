namespace QuickShare.Features.Shares.P2P;

public sealed class CreateP2PShareValidator : Validator<CreateP2PShareRequest>
{
    public CreateP2PShareValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty()
            .WithMessage("FileName is required.");

        RuleFor(x => x.FileSize)
            .GreaterThan(0)
            .WithMessage("FileSize must be positive.");

        RuleFor(x => x.BaseUrl)
            .NotEmpty()
            .WithMessage("BaseUrl is required.");
    }
}
