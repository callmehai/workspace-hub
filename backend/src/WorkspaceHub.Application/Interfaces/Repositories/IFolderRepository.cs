using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Repositories;

/// <summary>
/// Repository riÃªng cho Folder â€” truy váº¥n phá»©c táº¡p hÆ¡n GenericRepository
/// (include shares, Ä‘áº¿m items, láº¥y max sort order).
/// </summary>
public interface IFolderRepository : IGenericRepository<Folder>
{
    /// <summary>Folder do user sá»Ÿ há»¯u, kÃ¨m count ItemFolders (chÆ°a archived).</summary>
    Task<IReadOnlyList<Folder>> GetUserFoldersAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Folder Ä‘Æ°á»£c share cho user (Ä‘Ã£ accept, chÆ°a háº¿t háº¡n), kÃ¨m count ItemFolders.</summary>
    Task<IReadOnlyList<Folder>> GetSharedFoldersAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Láº¥y folder kÃ¨m Owner navigation â€” dÃ¹ng cho update/delete kiá»ƒm tra ownership.</summary>
    Task<Folder?> GetByIdWithOwnerAsync(Guid folderId, CancellationToken ct = default);

    /// <summary>Max SortOrder hiá»‡n táº¡i cá»§a user â€” dÃ¹ng khi táº¡o folder má»›i auto-increment.</summary>
    Task<int> GetMaxSortOrderAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Kiá»ƒm tra xem folder cÃ³ thuá»™c sá»Ÿ há»¯u cá»§a user khÃ´ng â€” dÃ¹ng cho ItemService list endpoint (nháº¹, trÃ¡nh load entity).</summary>
    Task<bool> ExistsByOwnerAsync(Guid folderId, Guid userId, CancellationToken ct = default);

    /// <summary>Kiá»ƒm tra item Ä‘Ã£ tá»“n táº¡i trong folder chÆ°a.</summary>
    Task<bool> ItemFolderExistsAsync(Guid itemId, Guid folderId, CancellationToken ct = default);

    /// <summary>Láº¥y position lá»›n nháº¥t trong folder Ä‘á»ƒ assign cho item má»›i.</summary>
    Task<int> GetMaxItemPositionAsync(Guid folderId, CancellationToken ct = default);

    /// <summary>Láº¥y ItemFolder junction Ä‘á»ƒ remove.</summary>
    Task<ItemFolder?> GetItemFolderAsync(Guid itemId, Guid folderId, CancellationToken ct = default);

    /// <summary>Láº¥y nhiá»u ItemFolder junction Ä‘á»ƒ remove.</summary>
    Task<IReadOnlyList<ItemFolder>> GetItemFoldersAsync(IEnumerable<Guid> itemIds, Guid folderId, CancellationToken ct = default);

    /// <summary>ThÃªm Item vÃ o Folder.</summary>
    Task AddItemFolderAsync(ItemFolder itemFolder, CancellationToken ct = default);

    /// <summary>ThÃªm nhiá»u Item vÃ o Folder.</summary>
    Task AddItemsFolderAsync(IEnumerable<ItemFolder> itemFolders, CancellationToken ct = default);

    /// <summary>Gá»¡ Item khá»i Folder.</summary>
    void RemoveItemFolder(ItemFolder itemFolder);

    /// <summary>Gá»¡ nhiá»u Item khá»i Folder.</summary>
    void RemoveItemsFolder(IEnumerable<ItemFolder> itemFolders);

    // â”€â”€â”€â”€â”€ Share operations â”€â”€â”€â”€â”€

    /// <summary>Láº¥y 1 FolderShare theo Id, include User + Folder navigations.</summary>
    Task<FolderShare?> GetShareByIdAsync(Guid shareId, CancellationToken ct = default);

    /// <summary>Danh sÃ¡ch shares cá»§a 1 folder (Ä‘á»ƒ owner xem), include SharedWithUser + Folder.</summary>
    Task<IReadOnlyList<FolderShare>> GetSharesByFolderAsync(Guid folderId, CancellationToken ct = default);

    /// <summary>Danh sÃ¡ch shares cá»§a user hiá»‡n táº¡i (shared-with-me), include Folder + Owner.</summary>
    Task<IReadOnlyList<FolderShare>> GetSharesForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Kiá»ƒm tra folder Ä‘Ã£ share vá»›i user chÆ°a (trÃ¡nh duplicate).</summary>
    Task<bool> ShareExistsAsync(Guid folderId, Guid userId, CancellationToken ct = default);

    /// <summary>Láº¥y share record cá»¥ thá»ƒ theo folderId vÃ  userId.</summary>
    Task<FolderShare?> GetShareByFolderAndUserAsync(Guid folderId, Guid userId, CancellationToken ct = default);

    /// <summary>ThÃªm FolderShare má»›i vÃ o DB.</summary>
    Task AddShareAsync(FolderShare share, CancellationToken ct = default);

    /// <summary>XÃ³a FolderShare (revoke hoáº·c decline).</summary>
    void RemoveShare(FolderShare share);

    /// <summary>Kiá»ƒm tra xem má»™t item cÃ³ náº±m trong thÆ° má»¥c Ä‘Æ°á»£c chia sáº» vá»›i user khÃ´ng.</summary>
    Task<bool> IsItemSharedWithUserAsync(Guid itemId, Guid userId, CancellationToken ct = default);

    /// <summary>Kiá»ƒm tra xem má»™t item cÃ³ náº±m trong thÆ° má»¥c Ä‘Æ°á»£c chia sáº» vá»›i user vá»›i quyá»n Editor khÃ´ng.</summary>
    Task<bool> IsItemSharedWithUserAsEditorAsync(Guid itemId, Guid userId, CancellationToken ct = default);

    /// <summary>Kiá»ƒm tra xem má»™t connection cÃ³ náº±m trong thÆ° má»¥c Ä‘Æ°á»£c chia sáº» vá»›i user vá»›i quyá»n Editor khÃ´ng.</summary>
    Task<bool> IsConnectionSharedWithUserAsEditorAsync(Guid connectionId, Guid userId, CancellationToken ct = default);
}
