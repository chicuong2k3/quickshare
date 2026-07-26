using LightResults;

namespace QuickShare.Features.Deliveries.Brand;

public record SetBrandCommand(Guid DeliveryId, string? Logo, string? Message, string? Accent);

public record BrandResponse(string? Logo, string? Message, string? Accent);

public sealed class SetBrandHandler
{
    public async Task<Result<BrandResponse>> Handle(SetBrandCommand cmd, AppDbContext db, IHttpContextAccessor http, CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);
        var userId = user.Id;
        var delivery = await db.Deliveries.FindAsync([cmd.DeliveryId], ct);
        if (delivery is null || delivery.SenderId != userId)
            return Result.Failure<BrandResponse>(new NotFoundError("Delivery", cmd.DeliveryId));

        if (user.Plan == PlanTierLimits.Free)
            return Result.Failure<BrandResponse>(new LightResults.Error("Branding requires Pro or Studio plan"));

        delivery.BrandLogo = cmd.Logo;
        delivery.BrandMessage = cmd.Message;
        delivery.BrandAccent = cmd.Accent;
        await db.SaveChangesAsync(ct);
        return Result.Success(new BrandResponse(delivery.BrandLogo, delivery.BrandMessage, delivery.BrandAccent));
    }
}
