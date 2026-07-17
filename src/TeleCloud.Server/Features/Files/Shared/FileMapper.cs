namespace TeleCloud.Server.Features.Files.Shared;

public static class FileMapper
{
    public static FileInfoResponse ToFileInfoResponse(this FileMetadata source)
    {
        return new FileInfoResponse(
            source.Id,
            source.Filename,
            source.ContentType,
            source.Size,
            source.FolderId,
            source.Folder?.Path ?? "/",
            source.Tags,
            source.CreatedAt
        );
    }
}
