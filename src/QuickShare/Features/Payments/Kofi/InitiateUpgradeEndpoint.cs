using QuickShare.Models;

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
            s.Description = "Creates a PaymentIntent with a unique code. The user must include this code in their " +
                "Ko-fi payment message so the webhook can match the payment to their account.";
            s.Responses[200] = "Payment code + Ko-fi URL + expected amount.";
            s.Responses[400] = "Invalid plan or billing.";
            s.Responses[401] = "User is not authenticated.";
        });
    }

    public override async Task HandleAsync(InitiateUpgradeRequest req, CancellationToken ct)
    {
        var plan = req.Plan?.ToLowerInvariant();
        var isYearly = string.Equals(req.Billing, "yearly", StringComparison.OrdinalIgnoreCase);

        if (plan is not "pro" and not "studio")
        {
            ThrowError("Invalid plan. Must be 'pro' or 'studio'.", 400);
            return;
        }

        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var config = Resolve<IConfiguration>();
        var pricingKey = plan switch
        {
            "pro" when isYearly => "Pricing:ProYearly",
            "pro" => "Pricing:ProMonthly",
            "studio" when isYearly => "Pricing:StudioYearly",
            _ => "Pricing:StudioMonthly"
        };

        var expectedAmount = config.GetValue<int>(pricingKey);
        var currency = config["Pricing:Currency"] ?? "$";
        var kofiPageUrl = config["Kofi:PageUrl"] ?? "https://ko-fi.com/miraclefoundation";

        var code = await GenerateUniqueCodeAsync(db, ct);
        var intent = new PaymentIntent
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Code = code,
            Plan = plan,
            Billing = isYearly ? "yearly" : "monthly",
            Amount = expectedAmount,
            Currency = currency,
            Status = PaymentIntentStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        db.PaymentIntents.Add(intent);
        await db.SaveChangesAsync(ct);

        var logger = Resolve<ILogger<InitiateUpgradeEndpoint>>();
        logger.LogInformation(
            "Payment intent created: user={UserId} code={Code} plan={Plan} billing={Billing} amount={Amount}",
            user.Id, code, plan, isYearly ? "yearly" : "monthly", expectedAmount);

        await Send.OkAsync(new
        {
            code,
            kofiPageUrl,
            expectedAmount,
            currency,
            plan,
            billing = isYearly ? "yearly" : "monthly",
            expiresAt = intent.ExpiresAt
        }, ct);
    }

    private static async Task<string> GenerateUniqueCodeAsync(AppDbContext db, CancellationToken ct)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        const int randomLen = 6;
        var random = Random.Shared;

        while (true)
        {
            var code = "QSC-" + new string(Enumerable.Range(0, randomLen)
                .Select(_ => chars[random.Next(chars.Length)])
                .ToArray());

            var exists = await db.PaymentIntents
                .AnyAsync(i => i.Code == code && i.Status == PaymentIntentStatus.Pending, ct);

            if (!exists)
                return code;
        }
    }
}

