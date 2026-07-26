using Riok.Mapperly.Abstractions;

namespace QuickShare.Features.Files.Shared;

[Mapper]
public static partial class FileMapper
{
    [MapPropertyFromSource(nameof(FileItemResponse.FolderName), Use = nameof(MapFolderName))]
    public static partial FileItemResponse ToFileItemResponse(this FileMetadata source);

    private static string MapFolderName(FileMetadata source)
    {
        return source.Folder?.Name ?? "/";
    }
}
