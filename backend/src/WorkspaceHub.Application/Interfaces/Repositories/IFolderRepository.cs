using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Repositories;

/// <summary>
/// Repository riêng cho Folder — truy vấn phức tạp hơn GenericRepository
/// (include shares, đếm items, lấy max sort order).
/// </summary>
public interface IFolderRepository : IGenericRepository<Folder>
{
    /// <summary>Folder do user sở hữu, kèm count ItemFolders (chưa archived).</summary>
    Task<IReadOnlyList<Folder>> GetUserFoldersAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Folder được share cho user (đã accept, chưa hết hạn), kèm count ItemFolders.</summary>
    Task<IReadOnlyList<Folder>> GetSharedFoldersAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Lấy folder kèm Owner navigation — dùng cho update/delete kiểm tra ownership.</summary>
    Task<Folder?> GetByIdWithOwnerAsync(Guid folderId, CancellationToken ct = default);

    /// <summary>Max SortOrder hiện tại của user — dùng khi tạo folder mới auto-increment.</summary>
    Task<int> GetMaxSortOrderAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Kiểm tra xem folder có thuộc sở hữu của user không — dùng cho ItemService list endpoint (nhẹ, tránh load entity).</summary>
    Task<bool> ExistsByOwnerAsync(Guid folderId, Guid userId, CancellationToken ct = default);

    /// <summary>Kiểm tra item đã tồn tại trong folder chưa.</summary>
    Task<bool> ItemFolderExistsAsync(Guid itemId, Guid folderId, CancellationToken ct = default);

    /// <summary>Lấy position lớn nhất trong folder để assign cho item mới.</summary>
    Task<int> GetMaxItemPositionAsync(Guid folderId, CancellationToken ct = default);

    /// <summary>Lấy ItemFolder junction để remove.</summary>
    Task<ItemFolder?> GetItemFolderAsync(Guid itemId, Guid folderId, CancellationToken ct = default);

    /// <summary>Lấy nhiều ItemFolder junction để remove.</summary>
    Task<IReadOnlyList<ItemFolder>> GetItemFoldersAsync(IEnumerable<Guid> itemIds, Guid folderId, CancellationToken ct = default);

    /// <summary>Thêm Item vào Folder.</summary>
    Task AddItemFolderAsync(ItemFolder itemFolder, CancellationToken ct = default);

    /// <summary>Thêm nhiều Item vào Folder.</summary>
    Task AddItemsFolderAsync(IEnumerable<ItemFolder> itemFolders, CancellationToken ct = default);

    /// <summary>Gỡ Item khỏi Folder.</summary>
    void RemoveItemFolder(ItemFolder itemFolder);

    /// <summary>Gỡ nhiều Item khỏi Folder.</summary>
    void RemoveItemsFolder(IEnumerable<ItemFolder> itemFolders);

    // ───── Share operations ─────

    /// <summary>Lấy 1 FolderShare theo Id, include User + Folder navigations.</summary>
    Task<FolderShare?> GetShareByIdAsync(Guid shareId, CancellationToken ct = default);

    /// <summary>Danh sách shares của 1 folder (để owner xem), include SharedWithUser + Folder.</summary>
    Task<IReadOnlyList<FolderShare>> GetSharesByFolderAsync(Guid folderId, CancellationToken ct = default);

    /// <summary>Danh sách shares của user hiện tại (shared-with-me), include Folder + Owner.</summary>
    Task<IReadOnlyList<FolderShare>> GetSharesForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Kiểm tra folder đã share với user chưa (tránh duplicate).</summary>
    Task<bool> ShareExistsAsync(Guid folderId, Guid userId, CancellationToken ct = default);

    /// <summary>Lấy share record cụ thể theo folderId và userId.</summary>
    Task<FolderShare?> GetShareByFolderAndUserAsync(Guid folderId, Guid userId, CancellationToken ct = default);

    /// <summary>Thêm FolderShare mới vào DB.</summary>
    Task AddShareAsync(FolderShare share, CancellationToken ct = default);

    /// <summary>Xóa FolderShare (revoke hoặc decline).</summary>
    void RemoveShare(FolderShare share);

    /// <summary>Kiểm tra xem một item có nằm trong thư mục được chia sẻ với user không.</summary>
    Task<bool> IsItemSharedWithUserAsync(Guid itemId, Guid userId, CancellationToken ct = default);

    /// <summary>Kiểm tra xem một item có nằm trong thư mục được chia sẻ với user với quyền Editor không.</summary>
    Task<bool> IsItemSharedWithUserAsEditorAsync(Guid itemId, Guid userId, CancellationToken ct = default);

    /// <summary>Kiểm tra xem một connection có nằm trong thư mục được chia sẻ với user với quyền Editor không.</summary>
    Task<bool> IsConnectionSharedWithUserAsEditorAsync(Guid connectionId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Kiểm tra một connection có nằm trong thư mục được chia sẻ với user (mọi quyền, đã accept) không —
    /// dùng cấp quyền ĐỌC cho item con của folder Drive được share (con không có junction riêng nên
    /// check theo connection, nhất quán với cơ chế list duyệt theo driveParentId).
    /// </summary>
    Task<bool> IsConnectionSharedWithUserAsync(Guid connectionId, Guid userId, CancellationToken ct = default);
}
