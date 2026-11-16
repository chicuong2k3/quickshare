namespace QuickShare.Infrastructure;

/// <summary>
/// Smash-style boarding queue evaluator for large files on Free/Anonymous tiers
/// (issue #41 download speed queue, issue #54 upload queue).
/// Paid tiers (Pro/Studio) and one-time Transfer Credit Passes (#85) always get
/// priority lane (no queue, 0s ETA).
/// </summary>
public static class BoardingQueuePolicy
{
    public const int BaseEtaSeconds = 10;
    public const int SecondsPerAdditionalSlot = 15;
    public const int MaxEtaSeconds = 180;
    private const long StepBytes = 100L * 1024 * 1024;

    public sealed record Decision(bool IsQueued, int Position, int EtaSeconds, bool SuppressAdInterstitial);

    public static Decision Evaluate(
        string? plan,
        long fileSizeBytes,
        bool hasTransferPass,
        QuickShareOptions options)
    {
        if (hasTransferPass || plan is PlanTierLimits.Pro or PlanTierLimits.Studio)
        {
            return new Decision(false, 0, 0, true);
        }

        var threshold = Math.Max(1L, options.FreeQueueThresholdBytes);
        if (fileSizeBytes < threshold)
        {
            return new Decision(false, 0, 0, false);
        }

        var extraSteps = (int)Math.Clamp((fileSizeBytes - threshold) / StepBytes, 0, 10);
        var position = 1 + extraSteps;
        var etaSeconds = Math.Min(MaxEtaSeconds, BaseEtaSeconds + extraSteps * SecondsPerAdditionalSlot);

        return new Decision(true, position, etaSeconds, options.DisableAdInterstitialWhenQueued);
    }
}
