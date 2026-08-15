using Hangfire.Dashboard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.FeatureFlags;

namespace QuickShare.Infrastructure.Jobs;

/// <summary>
/// Gates the Hangfire dashboard behind the same config-based admin list
/// (<see cref="AdminOptions.Emails"/>) as the rest of the admin surface.
/// The dashboard is NOT under the ASP.NET Core auth pipeline, so this filter is the
/// only thing between the dashboard and an anonymous caller.
/// </summary>
public sealed class AdminDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var options = httpContext.RequestServices.GetRequiredService<IOptions<AdminOptions>>();
        return httpContext.User.IsAdmin(options);
    }
}
