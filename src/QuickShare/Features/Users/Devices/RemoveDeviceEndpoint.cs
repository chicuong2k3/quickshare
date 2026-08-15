namespace QuickShare.Features.Users.Devices;

/// <summary>
/// Removes a known device from the account (issue #149). After removal the
/// device's next login is treated as new again — re-triggering the alert email.
/// </summary>
public sealed class RemoveDeviceEndpoint : EndpointWithoutRequest
{
    private const string DeviceIdRouteKey = "id";

    public override void Configure()
    {
        Delete("/api/me/devices/{id}");
        Description(b => b.WithTags(ApiTags.Users));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var deviceId = Route<Guid>(DeviceIdRouteKey);

        var device = await db.UserDevices
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == user.Id, ct);

        if (device is null)
        {
            ThrowError("Device not found", 404);
            return;
        }

        db.UserDevices.Remove(device);
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new { removed = true }, ct);
    }
}
