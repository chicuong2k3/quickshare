using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.FeatureFlags;

namespace QuickShare.Features.Admin.Flags;

public sealed class ListFlagsEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/admin/flags");
        Description(b => b.WithTags(ApiTags.Admin));
        Summary(s =>
        {
            s.Summary = "List all feature flags with their effective state";
            s.Description = "Admin only. Returns every configured flag with config default, DB override, and the resolved effective state.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!User.IsAdmin(Resolve<IOptions<AdminOptions>>()))
        {
            ThrowError("Forbidden", 403);
            return;
        }

        var flags = await Resolve<IFeatureFlagService>().ListAsync(ct);
        await Send.OkAsync(flags, ct);
    }
}