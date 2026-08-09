namespace QuickShare.Infrastructure.Constants;

public static class AdConstants
{
    public const int MaxDailyAds = 3;
    public const long RewardBandwidthBytes = 104_857_600L; // 100 MB
    public const int MaxDailyP2PUploads = 3;
    public const int P2pUploadsPerAd = 1;
    public const string AdMobScriptUrl = "https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js";
    public const string CssPrefix = "ad-";
    public const string RewardPromptRoute = "/ads/reward-prompt";
    public const string RewardCallbackRoute = "/api/ads/reward-callback";

    public const string ConsentCookieName = "ad-consent";
    public const string ConsentCookieValueGranted = "granted";
    public const string ConsentCookieValueDenied = "denied";
}
