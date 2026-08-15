namespace QuickShare.Features.Uploads.CommitR2;

public sealed class CommitR2Validator : Validator<CommitR2Request>
{
    public CommitR2Validator()
    {
        RuleFor(x => x.UploadId)
            .NotEmpty()
            .WithMessage("Upload ID is required.");

        RuleFor(x => x.ObjectKey)
            .NotEmpty()
            .WithMessage("Object key is required.");

        RuleFor(x => x.FileName)
            .NotEmpty()
            .WithMessage("File name is required.");

        RuleFor(x => x.FileSize)
            .GreaterThan(0)
            .WithMessage("File size must be positive.");

        RuleFor(x => x.Parts)
            .NotEmpty()
            .WithMessage("At least one part is required.");

        RuleFor(x => x.DeliverAt)
            .Must(d => d is null || d.Value > DateTime.UtcNow)
            .WithMessage("DeliverAt must be in the future.");

        RuleFor(x => x.Message)
            .Must(m => m is null || m.Trim().Length <= Delivery.MessageMaxLength)
            .WithMessage($"Message must be at most {Delivery.MessageMaxLength} characters.");
    }
}
