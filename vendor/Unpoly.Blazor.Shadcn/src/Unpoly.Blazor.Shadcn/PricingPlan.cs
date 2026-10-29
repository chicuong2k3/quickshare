namespace Unpoly.Blazor.Shadcn;

/// <summary>One plan in a PricingTable: its name and what it costs billed monthly and billed
/// yearly. The name is also the value the form submits as <c>plan</c>.</summary>
public sealed record PricingPlan(string Name, decimal MonthlyPrice, decimal YearlyPrice)
{
    /// <summary>Marks the plan to recommend: it carries a badge, and is the one selected when the
    /// table is given no selection of its own.</summary>
    public bool Popular { get; init; }
}

/// <summary>One row of a PricingTable's comparison: a feature and the first plan that includes
/// it. Plans are tiers, so every plan after that one includes it too.</summary>
public sealed record PricingFeature(string Name, string IncludedFrom);

/// <summary>One plan card in a GrowthBusiness or SubscriptionDetails section: its name, a line
/// about who it is for, both prices, and the features it lists.</summary>
public sealed record PricingTier(string Name, string Description, decimal MonthlyPrice, decimal YearlyPrice,
    IReadOnlyList<string> Features)
{
    /// <summary>The plan the section singles out — the dark card.</summary>
    public bool Featured { get; init; }

    /// <summary>Where the card's button goes. Null renders a plain button with nowhere to go,
    /// as upstream's is.</summary>
    public string? Href { get; init; }
}

/// <summary>How the pricing sections write a price: invariant, grouped, with cents only when
/// there are any. ui.js rolls one written price into another character by character, so both
/// must come from here.</summary>
internal static class PriceText
{
    public static string Of(decimal value) =>
        value.ToString(decimal.Round(value) == value ? "N0" : "N2", System.Globalization.CultureInfo.InvariantCulture);
}
