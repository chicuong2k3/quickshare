using Microsoft.AspNetCore.Http;

namespace TeleCloud.Server.Features.Files.Upload;

public class UploadFileRequest
{
    public IFormFile? File { get; set; }
    public Guid FolderId { get; set; }
}
