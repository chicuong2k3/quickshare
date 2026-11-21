using Riok.Mapperly.Abstractions;

namespace QuickShare.Features.Users.Shared;

[Mapper]
public static partial class UserMapper
{
    [MapperIgnoreSource(nameof(User.UpdatedAt))]
    [MapperIgnoreSource(nameof(User.EmailOnDownload))]
    [MapperIgnoreSource(nameof(User.TrackMyDownloads))]
    [MapperIgnoreSource(nameof(User.ReferredByUserId))]
    [MapperIgnoreSource(nameof(User.ColdStorageDays))]
    // Workspace seat plan override (issue #106) — bookkeeping for restoring the account's
    // own plan when a seat ends, never part of the profile it shows.
    [MapperIgnoreSource(nameof(User.PersonalPlan))]
    // The upload-by-email address (issue #104) has its own endpoint: it is a credential-ish
    // route into the account, not something to hand out with every profile render.
    [MapperIgnoreSource(nameof(User.EmailIngestToken))]
    // Cold storage (#314) never landed: User.ColdStorageDays only exists in the shape it
    // describes being removed, so this line goes with it — not before.
    [MapperIgnoreSource(nameof(User.ColdStorageDays))]
    public static partial ProfileResponse ToProfileResponse(this User source);
}
