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
            .WithMessage("At least one part number is required.");
    }
}
