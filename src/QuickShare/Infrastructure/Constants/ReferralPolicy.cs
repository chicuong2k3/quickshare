namespace QuickShare.Infrastructure.Constants;

/// <summary>Referral program policy (issue #15): cookie, reward size, caps, wire names.</summary>
public static class ReferralPolicy
{
    public const string CookieName = "qs_ref";
    public const string QueryParam = "r";
    public const int CookieDays = 30;
    public const int ReferralCodeLength = 8;

    /// <summary>Monthly bonus granted to BOTH sides when the referral converts.</summary>
    public const long BonusQuotaBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Total bonus a single account may accumulate.</summary>
    public const long MaxBonusQuotaBytes = 16L * 1024 * 1024 * 1024;
}
