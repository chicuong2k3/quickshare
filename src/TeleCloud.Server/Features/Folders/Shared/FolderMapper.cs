using Riok.Mapperly.Abstractions;

namespace TeleCloud.Server.Features.Folders.Shared;

[Mapper]
public static partial class FolderMapper
{
    public static partial FolderInfoResponse ToFolderInfoResponse(this Folder source);
}
