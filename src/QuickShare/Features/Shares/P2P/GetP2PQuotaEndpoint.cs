namespace QuickShare.Features.Shares.P2P;

public sealed class GetP2PQuotaEndpoint : EndpointWithoutRequest<GetP2PQuotaResponse>
{
    public override void Configure()
    {
        Get("/api/p2p/quota");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.P2p));
        Summary(s =>
        {
            s.Summary = "Get today's P2P upload quota status";
            s.Description = "Returns the caller's daily P2P share-link allowance and usage. Free/anonymous users have a fixed daily limit; paid plans are unlimited.";
            s.Responses[200] = "Quota status.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var principal = HttpContext.User;
        var isAuthenticated = principal.Identity?.IsAuthenticated == true;

        User? user = null;
        if (isAuthenticated)
        {
            try
            {
                user = await principal.GetUserAsync(db, ct);
            }
            catch (InvalidOperationException)
            {
                isAuthenticated = false;
            }
        }

        var plan = user?.Plan ?? PlanTierLimits.Anonymous;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var quota = new P2PQuotaService(db);
        var result = await quota.GetTodayAsync(
            isAuthenticated ? user!.Id : null,
            ipAddress,
            plan,
            ct);

        var response = new GetP2PQuotaResponse
        {
            Plan = plan,
            Unlimited = result.IsUnlimited,
            Limit = result.Limit,
            Used = result.Used,
            Unlocked = result.Unlocked,
            Allowance = result.Allowance,
            Remaining = result.Remaining,
            CanUnlockWithAd = isAuthenticated && plan == PlanTierLimits.Free
        };

        await Send.OkAsync(response, ct);
    }
}

public sealed class GetP2PQuotaResponse
{
    public string Plan { get; set; } = string.Empty;
    public bool Unlimited { get; set; }
    public int Limit { get; set; }
    public int Used { get; set; }
    public int Unlocked { get; set; }
    public int Allowance { get; set; }
    public int Remaining { get; set; }
    public bool CanUnlockWithAd { get; set; }
}
