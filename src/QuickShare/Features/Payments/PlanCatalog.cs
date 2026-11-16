namespace QuickShare.Features.Payments;

public sealed record PlanInfo(
    string Display,
    string MonthlyPricingKey,
    string YearlyPricingKey,
    int MonthlyDefaultAmount,
    int YearlyDefaultAmount,
    int MonthlyUsdAmount,
    int YearlyUsdAmount,
    int MonthlyEurAmount,
    int YearlyEurAmount);

/// <summary>Display + pricing metadata for the paid plans, including multi-currency tiers (issue #120).</summary>
public static class PlanCatalog
{
    public static readonly string[] SupportedCurrencies =
    [
        PricingKeys.DefaultCurrency,
        PricingKeys.CurrencyUsd,
        PricingKeys.CurrencyEur,
    ];

    public static readonly PlanInfo Pro = new(
        Display: "Pro",
        MonthlyPricingKey: PricingKeys.ProMonthly,
        YearlyPricingKey: PricingKeys.ProYearly,
        MonthlyDefaultAmount: 50000,
        YearlyDefaultAmount: 500000,
        MonthlyUsdAmount: 9,
        YearlyUsdAmount: 77,
        MonthlyEurAmount: 8,
        YearlyEurAmount: 69);

    public static readonly PlanInfo Studio = new(
        Display: "Studio",
        MonthlyPricingKey: PricingKeys.StudioMonthly,
        YearlyPricingKey: PricingKeys.StudioYearly,
        MonthlyDefaultAmount: 200000,
        YearlyDefaultAmount: 2000000,
        MonthlyUsdAmount: 29,
        YearlyUsdAmount: 239,
        MonthlyEurAmount: 26,
        YearlyEurAmount: 219);

    // ── Seat and overage pricing (issues #278, #141) ──
    public static int IncludedSeats(string plan) => plan switch
    {
        PlanTierLimits.Studio => 10,
        PlanTierLimits.Pro => 3,
        _ => 1,
    };

    public static decimal SeatMonthlyAmount(string plan) => plan switch
    {
        PlanTierLimits.Studio => 20000m,
        _ => 25000m,
    };

    public static decimal OverageGbAmount(string plan) => plan switch
    {
        PlanTierLimits.Studio => 5000m,
        _ => 0m,
    };

    public static bool AllowsOverage(string plan) => plan == PlanTierLimits.Studio;

    public static string Normalize(string? plan) =>
        plan?.ToLowerInvariant() == PlanTierLimits.Studio ? PlanTierLimits.Studio : PlanTierLimits.Pro;

    /// <summary>Normalizes a currency code to VND, USD or EUR (issue #120).</summary>
    public static string NormalizeCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            return PricingKeys.DefaultCurrency;

        var upper = currency.Trim().ToUpperInvariant();
        return upper switch
        {
            PricingKeys.CurrencyUsd or "$" => PricingKeys.CurrencyUsd,
            PricingKeys.CurrencyEur or "€" => PricingKeys.CurrencyEur,
            _ => PricingKeys.DefaultCurrency,
        };
    }

    /// <summary>Detects default currency from Cloudflare country or UI culture when none is explicitly chosen (issue #120).</summary>
    public static string DetectCurrency(string? requestedCurrency, string? countryCode, string? cultureName, string? configDefault = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedCurrency))
            return NormalizeCurrency(requestedCurrency);

        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            var cc = countryCode.Trim().ToUpperInvariant();
            if (cc == "VN") return PricingKeys.DefaultCurrency;
            if (cc is "DE" or "FR" or "NL" or "ES" or "IT" or "IE" or "BE" or "AT" or "PT" or "FI")
                return PricingKeys.CurrencyEur;
            return PricingKeys.CurrencyUsd;
        }

        if (!string.IsNullOrWhiteSpace(cultureName) && cultureName.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
            return PricingKeys.DefaultCurrency;

        return NormalizeCurrency(configDefault);
    }

    /// <summary>Reference FX rate snapshot relative to VND (issue #120).</summary>
    public static decimal ExchangeRateFor(string? currency) => NormalizeCurrency(currency) switch
    {
        PricingKeys.CurrencyUsd => 25000m,
        PricingKeys.CurrencyEur => 27000m,
        _ => 1m,
    };

    public static PlanInfo Get(string plan) =>
        Normalize(plan) == PlanTierLimits.Studio ? Studio : Pro;

    public static string PricingKey(string plan, bool isYearly)
    {
        var info = Get(plan);
        return isYearly ? info.YearlyPricingKey : info.MonthlyPricingKey;
    }

    public static int DefaultAmount(string plan, bool isYearly, string? currency = null)
    {
        var info = Get(plan);
        var normCurrency = NormalizeCurrency(currency);
        return (normCurrency, isYearly) switch
        {
            (PricingKeys.CurrencyUsd, false) => info.MonthlyUsdAmount,
            (PricingKeys.CurrencyUsd, true) => info.YearlyUsdAmount,
            (PricingKeys.CurrencyEur, false) => info.MonthlyEurAmount,
            (PricingKeys.CurrencyEur, true) => info.YearlyEurAmount,
            (_, true) => info.YearlyDefaultAmount,
            _ => info.MonthlyDefaultAmount,
        };
    }

    public static string FormatPrice(int amount, string? currency)
    {
        var norm = NormalizeCurrency(currency);
        return norm switch
        {
            PricingKeys.CurrencyUsd => $"${amount:N0}",
            PricingKeys.CurrencyEur => $"€{amount:N0}",
            _ => $"{amount:N0}₫",
        };
    }
}
