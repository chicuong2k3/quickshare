namespace QuickShare.Features.Uploads.StartR2;

public sealed class StartR2Validator : Validator<StartR2Request>
{
    public StartR2Validator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty()
            .WithMessage("File name is required.");

        RuleFor(x => x.FileSize)
            .GreaterThan(0)
            .WithMessage("File size must be positive.");

        RuleFor(x => x.ContentType)
            .NotEmpty()
            .WithMessage("Content type is required.");

        RuleFor(x => x.PartNumbers)
            .NotEmpty()
            .WithMessage("At least one part number is required.");
    }
}
