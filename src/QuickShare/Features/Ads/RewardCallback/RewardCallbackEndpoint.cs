using QuickShare.Infrastructure;

namespace QuickShare.Features.Ads.RewardCallback;

public sealed class RewardCallbackEndpoint : Endpoint<RewardCallbackRequest>
{
    public override void Configure()
    {
        Post("/api/ads/reward-callback");
        Description(b => b.WithTags(ApiTags.Ads));
        Summary(s =>
        {
            s.Summary = "Submit AdMob reward callback after rewarded ad completion.";
            s.Description = "Called by client JS after Google AdMob fires the reward event.";
            s.Responses[200] = "Reward granted.";
            s.Responses[400] = "Invalid or rejected reward token.";
            s.Responses[429] = "Daily ad limit reached.";
            s.Responses[502] = "Google SSV verification failed.";
        });
    }

    public override async Task HandleAsync(RewardCallbackRequest req, CancellationToken ct)
    {
        var principal = HttpContext.User;
        var user = await principal.GetUserAsync(Resolve<AppDbContext>(), ct);
        if (user is null)
        {
            HttpContext.Response.StatusCode = 401;
            return;
        }

        var handler = new RewardCallbackHandler(Resolve<AppDbContext>());
        var result = await handler.HandleAsync(user.Id, req.Token, req.RewardAmount, ct);

        var response = new RewardCallbackResponse
        {
            Success = result.IsSuccess,
            QuotaAddedMb = (int)(result.QuotaAddedBytes / (1024 * 1024)),
            QuotaNowMb = (int)(result.QuotaNowBytes / (1024 * 1024)),
            AdsRemainingToday = result.AdsRemainingToday,
            P2pUploadsUnlocked = result.P2pUploadsUnlocked,
            Error = result.Error
        };

        var statusCode = result.IsSuccess ? 200
            : result.Error?.Contains("limit", StringComparison.OrdinalIgnoreCase) == true ? 429
            : 400;

        await Send.ResponseAsync(response, statusCode, ct);
    }
}

public sealed class RewardCallbackRequest
{
    public string Token { get; set; } = string.Empty;
    public int RewardAmount { get; set; }
}

public sealed class RewardCallbackResponse
{
    public bool Success { get; set; }
    public int QuotaAddedMb { get; set; }
    public int QuotaNowMb { get; set; }
    public int AdsRemainingToday { get; set; }
    public int P2pUploadsUnlocked { get; set; }
    public string? Error { get; set; }
}
