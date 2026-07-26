namespace QuickShare.Features.Users.Profile;

public sealed class UpdateUserProfileValidator : Validator<UpdateUserProfileRequest>
{
    public UpdateUserProfileValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Name is required.");
    }
}
