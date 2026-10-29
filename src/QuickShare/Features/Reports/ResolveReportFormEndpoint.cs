using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Caching;
using QuickShare.Infrastructure.FeatureFlags;

namespace QuickShare.Features.Reports;

public sealed class ResolveReportFormEndpoint : EndpointWithoutRequest
{
    public const string FormRoute = "/admin/reports/resolve";
    public const string ReportIdField = "reportId";
    public const string ActionField = "action";

    public override void Configure()
    {
        Post(FormRoute);
        DontAutoSendResponse();
        Description(b => b.WithTags(ApiTags.Admin));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!User.IsAdmin(Resolve<IOptions<AdminOptions>>()))
        {
            await Send.ResponseAsync(null!, StatusCodes.Status403Forbidden, ct);
            return;
        }

        try
        {
            await Resolve<IAntiforgery>().ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            await Send.ResponseAsync(null!, StatusCodes.Status400BadRequest, ct);
            return;
        }

        var form = await HttpContext.Request.ReadFormAsync(ct);
        var action = form[ActionField].ToString();
        if (!Guid.TryParse(form[ReportIdField], out var reportId)
            || (action != ReportAction.Resolve && action != ReportAction.Suspend))
        {
            await Send.ResponseAsync(null!, StatusCodes.Status400BadRequest, ct);
            return;
        }

        var resolved = await ReportResolution.ResolveAsync(
            reportId, action, Resolve<AppDbContext>(), Resolve<CacheService>(), ct);
        if (!resolved)
        {
            await Send.ResponseAsync(null!, StatusCodes.Status404NotFound, ct);
            return;
        }

        HttpContext.Response.StatusCode = StatusCodes.Status303SeeOther;
        HttpContext.Response.Headers.Location = RoutePaths.AdminReports;
    }
}
