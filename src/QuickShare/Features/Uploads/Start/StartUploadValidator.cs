namespace QuickShare.Features.Uploads.Start;

public sealed class StartUploadValidator : Validator<StartUploadCommand>
{
    public StartUploadValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty()
            .WithMessage("File name is required.");

        RuleFor(x => x.FileSize)
            .GreaterThan(0)
            .WithMessage("File size must be positive.");
    }
}
