using QuickShare.Features.Users.Shared;

namespace QuickShare.Features.Users.Devices;

public sealed class ListDevicesEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/me/devices");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "List devices known to the signed-in account";
            s.Description = "New-device alert (issue #149) management surface. Only the authenticated user's own devices.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var devices = await db.UserDevices
            .AsNoTracking()
            .Where(d => d.UserId == user.Id)
            .OrderByDescending(d => d.LastSeenAt)
            .Select(d => new DeviceResponse(
                d.Id,
                d.UserAgentLabel,
                d.Name,
                d.FirstSeenAt,
                d.LastSeenAt
            ))
            .ToListAsync(ct);

        await Send.OkAsync(devices, ct);
    }
}

public record DeviceResponse(Guid Id, string UserAgentLabel, string? Name, DateTime FirstSeenAt, DateTime LastSeenAt);
