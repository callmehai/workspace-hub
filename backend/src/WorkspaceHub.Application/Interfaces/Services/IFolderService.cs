using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// Business logic cho Folder CRUD.
/// Service nhận/trả DTO, không trả entity ra ngoài (CONVENTIONS.md).
/// </summary>
public interface IFolderService
{
    /// <summary>Lấy folders owned + (tuỳ chọn) shared cho user. Filter archived mặc định.</summary>
    Task<IReadOnlyList<FolderResponse>> GetFoldersAsync(Guid userId, bool includeShared, CancellationToken ct = default);

    /// <summary>Tạo folder mới. Auto-assign SortOrder.</summary>
    Task<FolderResponse> CreateAsync(Guid userId, CreateFolderRequest request, CancellationToken ct = default);

    /// <summary>Cập nhật folder metadata. Chỉ Owner (403 nếu không đủ quyền).</summary>
    Task<FolderResponse> UpdateAsync(Guid userId, Guid folderId, UpdateFolderRequest request, CancellationToken ct = default);

    /// <summary>Xoá folder (hard delete). CASCADE ItemFolders + FolderShares. Chỉ Owner.</summary>
    Task DeleteAsync(Guid userId, Guid folderId, CancellationToken ct = default);

    /// <summary>Gắn item vào folder. Trả lỗi 409 nếu đã được gắn.</summary>
    Task<ItemFolderResponse> AddItemToFolderAsync(Guid userId, Guid folderId, AddItemToFolderRequest request, CancellationToken ct = default);

    /// <summary>Gắn nhiều item vào folder.</summary>
    Task AddItemsToFolderAsync(Guid userId, Guid folderId, AddItemsToFolderBulkRequest request, CancellationToken ct = default);

    /// <summary>Gỡ item khỏi folder.</summary>
    Task RemoveItemFromFolderAsync(Guid userId, Guid folderId, Guid itemId, CancellationToken ct = default);

    /// <summary>Gỡ nhiều item khỏi folder.</summary>
    Task RemoveItemsFromFolderAsync(Guid userId, Guid folderId, RemoveItemsFromFolderBulkRequest request, CancellationToken ct = default);
}
