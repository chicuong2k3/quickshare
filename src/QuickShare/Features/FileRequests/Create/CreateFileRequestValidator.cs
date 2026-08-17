using QuickShare.Data.Entities;

namespace QuickShare.Features.FileRequests.Create;

public sealed class CreateFileRequestValidator : Validator<CreateFileRequestRequest>
{
    public CreateFileRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty()
            .WithMessage("A title is required.")
            .MaximumLength(FileRequest.TitleMaxLength)
            .WithMessage($"Title must be {FileRequest.TitleMaxLength} characters or fewer.");

        RuleFor(x => x.Message)
            .MaximumLength(FileRequest.MessageMaxLength)
            .WithMessage($"Message must be {FileRequest.MessageMaxLength} characters or fewer.");
    }
}
