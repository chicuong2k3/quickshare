using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Infrastructure.HealthChecks;

public sealed class EmailConfigHealthCheck(IOptions<EmailOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(options.Value.IsConfigured
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded("Email not configured"));
    }
}
