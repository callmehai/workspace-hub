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
}
