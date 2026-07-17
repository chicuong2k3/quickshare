namespace TeleCloud.Server.Features.TempShares.Shared;

public static class TempShareMapper
{
    public static TempShareResponse ToTempShareResponse(this TempShare source, string link, string downloadUrl)
    {
        return new TempShareResponse(
            source.Id,
            link,
            source.File?.Filename ?? string.Empty,
            source.File?.Size ?? 0,
            source.ExpiresAt,
            downloadUrl
        );
    }

    public static TempShareInfoResponse ToTempShareInfoResponse(this TempShare source, DateTime now)
    {
        return new TempShareInfoResponse(
            source.Id,
            source.File?.Filename ?? string.Empty,
            source.File?.Size ?? 0,
            source.File?.ContentType ?? "application/octet-stream",
            source.ExpiresAt,
            TempSharePolicy.IsExpired(source, now)
        );
    }
}
