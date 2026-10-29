using System.Text.Json;
using QuickShare.Features.Users.Shared;

namespace QuickShare.Features.Users.Devices;

internal static class DeviceRemoval
{
    public static async Task<bool> RemoveAsync(AppDbContext db, User user, Guid deviceId, HttpContext context, CancellationToken ct)
    {
        var device = await db.UserDevices.FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == user.Id, ct);
        if (device is null)
        {
            return false;
        }

        var metadata = JsonSerializer.Serialize(new { device.UserAgentLabel });
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.SessionRevoked, userAgent, ip, ct, metadata);
        db.UserDevices.Remove(device);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
