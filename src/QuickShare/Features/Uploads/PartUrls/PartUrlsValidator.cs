namespace QuickShare.Features.Uploads.PartUrls;

public sealed class PartUrlsValidator : Validator<PartUrlsRequest>
{
    public PartUrlsValidator()
    {
        RuleFor(x => x.UploadId)
            .NotEmpty()
            .WithMessage("Upload ID is required.");

        RuleFor(x => x.PartNumbers)
            .NotEmpty()
            .WithMessage("At least one part number is required.")
            .Must(parts => parts.All(p => p is >= 1 and <= StartR2.StartR2Handler.MaxPartNumber))
            .WithMessage($"Part numbers must be between 1 and {StartR2.StartR2Handler.MaxPartNumber}.");
    }
}
