namespace QuickShare.Features.Uploads.AbortR2;

public sealed class AbortR2Validator : Validator<AbortR2Request>
{
    public AbortR2Validator()
    {
        RuleFor(x => x.UploadId)
            .NotEmpty()
            .WithMessage("Upload ID is required.");

        RuleFor(x => x.ObjectKey)
            .NotEmpty()
            .WithMessage("Object key is required.");
    }
}
