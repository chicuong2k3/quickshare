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
