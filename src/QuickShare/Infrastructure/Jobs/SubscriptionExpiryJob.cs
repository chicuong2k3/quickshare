using Hangfire;
using QuickShare.Features.Payments.Subscriptions;

namespace QuickShare.Infrastructure.Jobs;

public sealed class SubscriptionExpiryJob(IServiceScopeFactory scopeFactory, ILogger<SubscriptionExpiryJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task CheckAndExpireAsync()
    {
        var ct = CancellationToken.None;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var expired = await SubscriptionService.ExpireStaleAsync(db, ct);

        if (expired > 0)
            logger.LogInformation("SubscriptionExpiryJob expired {Count} subscriptions", expired);
    }
}
