using Riok.Mapperly.Abstractions;

namespace QuickShare.Features.Users.Shared;

[Mapper]
public static partial class UserMapper
{
    [MapperIgnoreSource(nameof(User.UpdatedAt))]
    [MapperIgnoreSource(nameof(User.EmailOnDownload))]
    [MapperIgnoreSource(nameof(User.TrackMyDownloads))]
    [MapperIgnoreSource(nameof(User.ReferredByUserId))]
    public static partial ProfileResponse ToProfileResponse(this User source);
}
