using LightResults;

namespace QuickShare.Server.Features.Deliveries.Brand;

public record GetBrandQuery(string ShareCode);

public sealed class GetBrandHandler
{
    public async Task<Result<BrandResponse>> Handle(GetBrandQuery query, AppDbContext db, CancellationToken ct)
    {
        var delivery = await db.Deliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ShareCode == query.ShareCode, ct);

        if (delivery is null)
            return Result.Failure<BrandResponse>(new LightResults.Error("not_found"));

        return Result.Success(new BrandResponse(delivery.BrandLogo, delivery.BrandMessage, delivery.BrandAccent));
    }
}
