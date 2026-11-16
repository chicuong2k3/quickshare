using Microsoft.AspNetCore.Antiforgery;

namespace QuickShare.Features.Users.Devices;

public sealed class RemoveDeviceFormEndpoint : EndpointWithoutRequest
{
    public const string DeviceIdField = "deviceId";
    public const string ReturnToField = "returnTo";

    public override void Configure()
    {
        Post(RoutePaths.AccountDeviceRemove);
        DontAutoSendResponse();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            await Resolve<IAntiforgery>().ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var form = await HttpContext.Request.ReadFormAsync(ct);
        if (!Guid.TryParse(form[DeviceIdField].ToString(), out var deviceId))
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);
        if (!await DeviceRemoval.RemoveAsync(db, user, deviceId, HttpContext, ct))
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var returnTo = form[ReturnToField].ToString();
        var destination = returnTo == RoutePaths.AccountDevices ? RoutePaths.AccountDevices : RoutePaths.Account;
        HttpContext.Response.StatusCode = StatusCodes.Status303SeeOther;
        HttpContext.Response.Headers.Location = destination;
    }
}
