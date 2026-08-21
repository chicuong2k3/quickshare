namespace QuickShare.Features.Payments;

public sealed record PlanInfo(
    string Display,
    string MonthlyPricingKey,
    string YearlyPricingKey,
    int MonthlyDefaultAmount,
    int YearlyDefaultAmount);

/// <summary>Display + pricing metadata for the paid plans. All user-facing copy is localized in SharedLoc.</summary>
public static class PlanCatalog
{
    public static readonly PlanInfo Pro = new(
        Display: "Pro",
        MonthlyPricingKey: PricingKeys.ProMonthly,
        YearlyPricingKey: PricingKeys.ProYearly,
        MonthlyDefaultAmount: 50000,
        YearlyDefaultAmount: 500000);

    public static readonly PlanInfo Studio = new(
        Display: "Studio",
        MonthlyPricingKey: PricingKeys.StudioMonthly,
        YearlyPricingKey: PricingKeys.StudioYearly,
        MonthlyDefaultAmount: 200000,
        YearlyDefaultAmount: 2000000);

    // ── Seat and overage pricing (issues #278, #141) ──
    // Kept here rather than in PlanTierLimits: those are usage caps read on every upload,
    // these are prices read at billing time, and mixing them invites a hot path to depend
    // on a number that only accounting should care about.

    /// <summary>Seats a plan includes before per-seat billing starts. Free has no workspace.</summary>
    public static int IncludedSeats(string plan) => plan switch
    {
        PlanTierLimits.Studio => 10,
        PlanTierLimits.Pro => 3,
        _ => 1,
    };

    /// <summary>Monthly price of one seat past the included pool.</summary>
    public static decimal SeatMonthlyAmount(string plan) => plan switch
    {
        PlanTierLimits.Studio => 20000m,
        _ => 25000m,
    };

    /// <summary>
    /// Price per GB-month past the plan pool (issue #141). Only Studio meters — every other
    /// tier hard-blocks at 100%, so a rate for them would be a price nobody can ever pay.
    /// </summary>
    public static decimal OverageGbAmount(string plan) => plan switch
    {
        PlanTierLimits.Studio => 5000m,
        _ => 0m,
    };

    /// <summary>Whether a plan keeps working past its quota instead of hard-blocking.</summary>
    public static bool AllowsOverage(string plan) => plan == PlanTierLimits.Studio;

    /// <summary>Maps arbitrary user input to a known paid plan; anything unknown falls back to Pro.</summary>
    public static string Normalize(string? plan) =>
        plan?.ToLowerInvariant() == PlanTierLimits.Studio ? PlanTierLimits.Studio : PlanTierLimits.Pro;

    public static PlanInfo Get(string plan) =>
        Normalize(plan) == PlanTierLimits.Studio ? Studio : Pro;

    public static string PricingKey(string plan, bool isYearly)
    {
        var info = Get(plan);
        return isYearly ? info.YearlyPricingKey : info.MonthlyPricingKey;
    }

    public static int DefaultAmount(string plan, bool isYearly)
    {
        var info = Get(plan);
        return isYearly ? info.YearlyDefaultAmount : info.MonthlyDefaultAmount;
    }

    public static string FormatPrice(int amount, string? currency)
    {
        currency ??= PricingKeys.DefaultCurrency;
        return currency == PricingKeys.DefaultCurrency
            ? $"{amount:N0}₫"
            : $"{currency}{amount}";
    }
}
