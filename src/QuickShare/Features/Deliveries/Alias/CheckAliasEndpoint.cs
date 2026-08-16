using System.Text.RegularExpressions;

namespace QuickShare.Features.Deliveries.Alias;

public record CheckAliasResponse(bool Available);

/// <summary>Live availability probe for the alias input (issue #311). Sender-auth only.</summary>
public sealed partial class CheckAliasEndpoint : EndpointWithoutRequest<CheckAliasResponse>
{
    private const string AliasQueryKey = "alias";

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*[a-z0-9]$")]
    private static partial Regex AliasPattern();

    public override void Configure()
    {
        Get("/api/deliveries/alias-check");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s => s.Summary = "Check whether a delivery alias is free");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var raw = Query<string>(AliasQueryKey, isRequired: false) ?? string.Empty;
        var alias = raw.Trim().ToLowerInvariant();

        var valid = alias.Length is >= Delivery.AliasMinLength and <= Delivery.AliasMaxLength
            && AliasPattern().IsMatch(alias)
            && !SetAliasHandler.ReservedAliases.Contains(alias);

        var available = valid && !await Resolve<AppDbContext>().Deliveries
            .AsNoTracking()
            .AnyAsync(d => d.Alias == alias, ct);

        await Send.OkAsync(new CheckAliasResponse(available), ct);
    }
}
