namespace QuickShare.Features.Users.Devices;

using System.Text.Json;
using QuickShare.Features.Users.Shared;

/// <summary>
/// Removes a known device from the account (issue #149). After removal the
/// device's next login is treated as new again — re-triggering the alert email.
/// The revoke is also recorded on the security audit trail (issue #126).
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

        // Security audit trail (issue #126): the revoked device's label rides in
        // the metadata so the timeline stays readable after the row is gone.
        var metadata = JsonSerializer.Serialize(new { device.UserAgentLabel });
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.SessionRevoked, userAgent, ip, ct, metadata);

        db.UserDevices.Remove(device);
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new { removed = true }, ct);
    }
}
