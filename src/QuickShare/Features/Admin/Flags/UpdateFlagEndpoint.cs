using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.FeatureFlags;

namespace QuickShare.Features.Admin.Flags;

public sealed class UpdateFlagEndpoint : Endpoint<UpdateFlagRequest>
{
    public override void Configure()
    {
        Put("/api/admin/flags/{Key}");
        Description(b => b.WithTags(ApiTags.Admin));
        Summary(s =>
        {
            s.Summary = "Create or update an admin override for a feature flag";
            s.Description = "Admin only. Overrides win over the appsettings default until changed back. RolloutPercent is a 0-100 deterministic bucket; anonymous callers only see a flag at 100%.";
        });
    }

    public override async Task HandleAsync(UpdateFlagRequest req, CancellationToken ct)
    {
        if (!User.IsAdmin(Resolve<IOptions<AdminOptions>>()))
        {
            ThrowError("Forbidden", 403);
            return;
        }

        var key = Route<string>("Key")!;
        var flags = await Resolve<IFeatureFlagService>().ListAsync(ct);
        if (flags.All(f => f.Key != key))
        {
            ThrowError($"Unknown flag: {key}", 400);
            return;
        }

        if (req.RolloutPercent is < 0 or > 100)
        {
            ThrowError("RolloutPercent must be between 0 and 100", 400);
            return;
        }

        await Resolve<IFeatureFlagService>().SetOverrideAsync(key, req.Enabled, req.RolloutPercent, ct);
        await Send.NoContentAsync(ct);
    }
}

public class UpdateFlagRequest
{
    public bool Enabled { get; set; }
    public int RolloutPercent { get; set; } = 100;
}