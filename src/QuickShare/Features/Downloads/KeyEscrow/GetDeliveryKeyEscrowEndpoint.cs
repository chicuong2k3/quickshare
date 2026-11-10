namespace QuickShare.Features.Downloads.KeyEscrow;

public sealed record DeliveryKeyEscrowDto(
    string ShareCode,
    string EncryptionMode,
    string? EncryptionKdfSalt,
    int? EncryptionKdfIterations,
    string? EncryptedKeyEscrow);

/// <summary>GET /api/downloads/{ShareCode}/key-escrow — return opt-in wrapped key escrow blob for recovery (issue #313).</summary>
public sealed class GetDeliveryKeyEscrowEndpoint : EndpointWithoutRequest<DeliveryKeyEscrowDto>
{
    public override void Configure()
    {
        Get(RoutePaths.DeliveryKeyEscrowApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Downloads));
        Summary(s => s.Summary = "Retrieve opt-in wrapped key escrow metadata for an encrypted delivery");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var shareCode = Route<string>("ShareCode") ?? string.Empty;
        var db = Resolve<AppDbContext>();

        var delivery = await db.Deliveries
            .AsNoTracking()
            .ByCode(shareCode)
            .FirstOrDefaultAsync(ct);

        if (delivery is null || delivery.DeletedAt is not null || delivery.Status == DeliveryStatus.PendingApproval)
        {
            ThrowError(ErrorCodes.DeliveryNotFound, StatusCodes.Status404NotFound);
            return;
        }

        await Send.OkAsync(new DeliveryKeyEscrowDto(
            delivery.ShareCode,
            string.IsNullOrEmpty(delivery.EncryptionMode) ? DeliveryEncryptionMode.FragmentKey : delivery.EncryptionMode,
            delivery.EncryptionKdfSalt,
            delivery.EncryptionKdfIterations,
            delivery.EncryptedKeyEscrow), ct);
    }
}
