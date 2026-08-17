namespace QuickShare.Features.Uploads.UploadFromUrl;

public sealed class UploadFromUrlValidator : Validator<UploadFromUrlRequest>
{
    public UploadFromUrlValidator()
    {
        RuleFor(x => x.Url)
            .NotEmpty().WithMessage(ErrorCodes.UrlInvalid)
            .MaximumLength(2048).WithMessage(ErrorCodes.UrlInvalid);
    }
}
