using Riok.Mapperly.Abstractions;

namespace QuickShare.Features.Folders.Shared;

[Mapper]
public static partial class FolderMapper
{
    [MapperIgnoreTarget(nameof(FolderItemResponse.FileCount))]
    public static partial FolderItemResponse ToFolderItemResponse(this Folder source);
}
