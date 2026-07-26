namespace QuickShare.Features.Payments.Kofi;

public sealed class InitiateUpgradeRequest
{
    public string Plan { get; set; } = string.Empty;
    public string Billing { get; set; } = string.Empty;
}

public sealed class InitiateUpgradeEndpoint : Endpoint<InitiateUpgradeRequest>
{
    public override void Configure()
    {
        Post("/api/upgrade/initiate");
        Description(b => b.WithTags("Payments"));
        Summary(s =>
        {
            s.Summary = "Initiate a Ko-fi plan upgrade";
            s.Description = "Returns a redirect URL to the Ko-fi product page for the selected plan and billing period.";
            s.Responses[200] = "Redirect URL returned via HX-Redirect header.";
            s.Responses[400] = "Invalid plan or missing product configuration.";
            s.Responses[401] = "User is not authenticated.";
        });
    }

    public override async Task HandleAsync(InitiateUpgradeRequest req, CancellationToken ct)
    {
        var plan = req.Plan?.ToLowerInvariant();
        var isYearly = string.Equals(req.Billing, "yearly", StringComparison.OrdinalIgnoreCase);
        var billing = isYearly ? "Yearly" : "Monthly";
        var planCapitalized = plan == "pro" ? "Pro" : "Studio";
        var productKey = $"Kofi:Products:{planCapitalized}{billing}";

        var config = Resolve<IConfiguration>();
        var productSlug = config[productKey];

        if (string.IsNullOrEmpty(productSlug))
            ThrowError($"Ko-fi product not configured for {planCapitalized} {billing}.", 500);

        var returnUrl = config["Kofi:ReturnUrl"] ?? "/";
        var kofiUrl = $"https://ko-fi.com/{productSlug}?return_url={returnUrl}";

        var logger = Resolve<ILogger<InitiateUpgradeEndpoint>>();
        logger.LogInformation("Initiating Ko-fi upgrade: plan={Plan} billing={Billing} url={Url}", plan, billing, kofiUrl);

        await Send.OkAsync(new { redirectUrl = kofiUrl }, ct);
    }
}
