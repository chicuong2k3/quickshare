using Riok.Mapperly.Abstractions;

namespace QuickShare.Features.Users.Shared;

[Mapper]
public static partial class UserMapper
{
    [MapperIgnoreSource(nameof(User.UpdatedAt))]
    [MapperIgnoreSource(nameof(User.EmailOnDownload))]
    [MapperIgnoreSource(nameof(User.TrackMyDownloads))]
    [MapperIgnoreSource(nameof(User.ReferredByUserId))]
    // Retention policy (issue #161) is an account compliance setting surfaced on the
    // security card, not profile identity — same treatment as the notification prefs above.
    [MapperIgnoreSource(nameof(User.RetentionDays))]
    [MapperIgnoreSource(nameof(User.RetentionNoExtend))]
    // Workspace seat plan override (issue #106) — bookkeeping for restoring the account's
    // own plan when a seat ends, never part of the profile it shows.
    [MapperIgnoreSource(nameof(User.PersonalPlan))]
    // Storage residency (issue #119) is an account setting, not profile identity.
    [MapperIgnoreSource(nameof(User.PreferredStorageRegion))]
    public static partial ProfileResponse ToProfileResponse(this User source);
}
