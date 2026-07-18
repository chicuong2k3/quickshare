using Microsoft.AspNetCore.Http;

namespace TeleCloud.Server.Features.TempShares.Create;

public class CreateTempShareRequest
{
    public IFormFile? File { get; set; }
    public string? Expiry { get; set; }
}

public sealed class CreateTempShareEndpoint : Endpoint<CreateTempShareRequest, TempShareResponse>
{
    public override void Configure()
    {
        Post("/api/temp");
        AllowFileUploads();
        Description(b => b.WithTags("TempShares"));
        Summary(s =>
        {
            s.Summary = "Create an anonymous temp share";
            s.Description = "Uploads a single file (multipart/form-data) to the Telegram storage channel, creates a FileMetadata in the root folder and a TempShare with an expiry timestamp. Returns a share link + download URL.";
            s.Responses[201] = "Share created; body contains id, link, filename, size, expiresAt, downloadUrl.";
            s.Responses[400] = "No file was included in the request, or the expiry value was invalid.";
            s.Responses[413] = "File exceeds the 2 GB upload limit.";
        });
    }

    public override async Task HandleAsync(CreateTempShareRequest req, CancellationToken ct)
    {
        if (req.File is null || req.File.Length == 0)
        {
            ThrowError("No file uploaded");
            return;
        }

        if (req.File.Length > TempSharePolicy.MaxUploadBytes)
        {
            await HttpContext.Response.SendAsync(new { error = "File exceeds the 2 GB upload limit." }, 413, cancellation: ct);
            return;
        }

        var duration = TempSharePolicy.TryParseExpiry(req.Expiry);
        if (duration is null)
        {
            ThrowError("Invalid expiry value. Allowed values: '24h', '7d' (default '24h').");
            return;
        }

        using var ms = new MemoryStream();
        await req.File.CopyToAsync(ms, ct);

        var request = HttpContext.Request;
        var schemeHost = $"{request.Scheme}://{request.Host.Value}{request.PathBase.Value}";

        var command = new CreateTempShareCommand(
            ms.ToArray(),
            req.File.FileName,
            req.File.ContentType,
            duration.Value,
            schemeHost);

        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<TempShareResponse>>(command, ct);

        if (result.IsSuccess(out var value))
            await HttpContext.Response.SendAsync(value, 201, cancellation: ct);
        else if (result.IsFailure(out var error))
        {
            if (error is ForbiddenError)
            {
                await HttpContext.Response.SendAsync(
                    new { error = "forbidden", message = error.Message }, 403, cancellation: ct);
                return;
            }
            ThrowError(error.Message);
        }
    }
}
