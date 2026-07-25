using QuickShare.Shared.Models;
using Refit;

namespace QuickShare.Web.Api;

public interface IQuickShareApi
{
    // ── Users ──
    [Post("/api/users/register")]
    Task<ProfileResponse> RegisterAsync([Body] RegisterRequest request);

    [Get("/api/users/me/profile")]
    Task<ProfileResponse> GetMyProfileAsync();

    [Put("/api/users/me/profile")]
    Task<ProfileResponse> UpdateMyProfileAsync([Body] UpdateProfileRequest request);

    // ── Folders ──
    [Get("/api/folders")]
    Task<PagedResult<FolderItemResponse>> GetFoldersAsync([Query] int pageSize);

    [Get("/api/folders/{id}")]
    Task<FolderItemResponse> GetFolderAsync(Guid id);

    // ── Files ──
    [Get("/api/files")]
    Task<PagedResult<FileItemResponse>> GetFilesAsync(
        [Query] int pageSize,
        [Query] Guid? folderId = null,
        [Query] string? search = null
    );

    // ── Account ──
    [Get("/api/account/profile")]
    Task<ProfileResponse> GetAccountProfileAsync();
}
