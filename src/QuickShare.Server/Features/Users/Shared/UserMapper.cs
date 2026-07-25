using Riok.Mapperly.Abstractions;

namespace QuickShare.Server.Features.Users.Shared;

[Mapper]
public static partial class UserMapper
{
    [MapperIgnoreSource(nameof(User.UpdatedAt))]
    public static partial ProfileResponse ToProfileResponse(this User source);
}
