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
            .WithMessage("At least one part number is required.")
            .Must((req, parts) => parts.Count <= StartR2Handler.MaxPartsFor(req.FileSize))
            .WithMessage(req =>
                $"Part count {req.PartNumbers.Count} exceeds what a {req.FileSize}-byte file can need")
            .Must(parts => parts.All(p => p is >= 1 and <= StartR2Handler.MaxPartNumber))
            .WithMessage($"Part numbers must be between 1 and {StartR2Handler.MaxPartNumber}.");
    }
}
