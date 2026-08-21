using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace QuickShare.Infrastructure.HealthChecks;

public sealed class R2HealthCheck(S3CompatibleStorageProvider storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await storage.PingAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("R2 unreachable", ex);
        }
    }
}
