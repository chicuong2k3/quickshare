namespace QuickShare.Server.Features.Folders.Shared;

public record FolderInfoResponse(
    Guid Id,
    string Name,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid? OwnerId = null,
    int FileCount = 0
);
