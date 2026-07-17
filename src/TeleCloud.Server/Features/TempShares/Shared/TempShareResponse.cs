namespace TeleCloud.Server.Features.TempShares.Shared;

public record TempShareResponse(
    Guid Id,
    string Link,
    string Filename,
    long Size,
    DateTime ExpiresAt,
    string DownloadUrl
);

public record TempShareInfoResponse(
    Guid Id,
    string Filename,
    long Size,
    string ContentType,
    DateTime ExpiresAt,
    bool Expired
);
