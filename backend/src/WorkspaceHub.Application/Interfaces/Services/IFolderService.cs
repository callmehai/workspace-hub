using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// Business logic cho Folder CRUD và Sharing.
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

    // ───── Sharing ─────

    /// <summary>
    /// Mời bạn bè vào folder với quyền Viewer hoặc Editor.
    /// Chỉ Owner được mời. FriendUserId phải là bạn bè đã accept.
    /// 403 không phải owner, 422 không phải bạn bè hoặc đã share rồi.
    /// </summary>
    Task<FolderShareDto> InviteShareAsync(Guid folderId, Guid requestingUserId, InviteFolderShareRequest request, CancellationToken ct = default);

    /// <summary>
    /// Danh sách shares của folder (để owner xem ai có quyền).
    /// Chỉ Owner được xem. 403 nếu không phải owner.
    /// </summary>
    Task<IReadOnlyList<FolderShareDto>> GetSharesForFolderAsync(Guid folderId, Guid requestingUserId, CancellationToken ct = default);

    /// <summary>
    /// Đổi quyền của 1 share (Viewer ↔ Editor).
    /// Chỉ Owner. Share phải thuộc folder này.
    /// </summary>
    Task<FolderShareDto> UpdateShareRoleAsync(Guid folderId, Guid shareId, Guid requestingUserId, UpdateFolderShareRequest request, CancellationToken ct = default);

    /// <summary>
    /// Revoke share (xoá row). Chỉ Owner.
    /// </summary>
    Task RevokeShareAsync(Guid folderId, Guid shareId, Guid requestingUserId, CancellationToken ct = default);

    /// <summary>
    /// Danh sách folder được share với user hiện tại (đã accept).
    /// GET /api/folders/shared-with-me.
    /// </summary>
    Task<IReadOnlyList<SharedFolderDto>> GetFoldersSharedWithMeAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Chấp nhận invite share. Chỉ người được share (SharedWithUserId).
    /// AcceptedAt = UtcNow.
    /// </summary>
    Task<FolderShareDto> AcceptShareAsync(Guid shareId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Từ chối invite share → xoá row.
    /// Chỉ người được share.
    /// </summary>
    Task DeclineShareAsync(Guid shareId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Rời khỏi folder được chia sẻ. Xoá record FolderShare tương ứng.
    /// </summary>
    Task LeaveFolderAsync(Guid folderId, Guid userId, CancellationToken ct = default);
}

