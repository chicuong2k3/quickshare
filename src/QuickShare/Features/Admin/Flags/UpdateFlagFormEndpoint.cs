using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.FeatureFlags;

namespace QuickShare.Features.Admin.Flags;

public sealed class UpdateFlagFormEndpoint : EndpointWithoutRequest
{
    public const string FormRoute = "/admin/flags/update";
    public const string KeyField = "Key";
    public const string EnabledField = "Enabled";
    public const string RolloutField = "RolloutPercent";

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
        var key = form[KeyField].ToString();
        var service = Resolve<IFeatureFlagService>();
        var flags = await service.ListAsync(ct);
        if (flags.All(flag => flag.Key != key)
            || !int.TryParse(form[RolloutField], out var rollout)
            || rollout is < 0 or > 100)
        {
            await Send.ResponseAsync(null!, StatusCodes.Status400BadRequest, ct);
            return;
        }

        await service.SetOverrideAsync(key, form.ContainsKey(EnabledField), rollout, ct);
        HttpContext.Response.StatusCode = StatusCodes.Status303SeeOther;
        HttpContext.Response.Headers.Location = RoutePaths.AdminFlags;
    }
}
