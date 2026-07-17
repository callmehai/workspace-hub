using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation cho <see cref="IFolderRepository"/>.
/// AsNoTracking cho read-only queries. AsSplitQuery khi load collection → tránh cartesian explosion / N+1.
/// </summary>
public class FolderRepository : GenericRepository<Folder>, IFolderRepository
{
    public FolderRepository(AppDbContext db) : base(db) { }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Folder>> GetUserFoldersAsync(Guid userId, CancellationToken ct = default)
    {
        // Owner (ref) cho OwnerName, ItemFolders chỉ để đếm ItemCount.
        // KHÔNG Include FolderShares: folder do user sở hữu → FolderService map permission = "Owner",
        // không bao giờ đọc FolderShares (xem FolderService.MapToResponse). Include nó vừa thừa vừa
        // gây cartesian explosion (rows = folders × itemFolders × folderShares).
        // AsSplitQuery: tách collection ItemFolders ra query riêng, không nhân đôi hàng folder/owner.
        return await Set
            .AsNoTracking()
            .Include(f => f.Owner)
            .Include(f => f.ItemFolders)
            .Where(f => f.OwnerId == userId && !f.IsArchived)
            .OrderBy(f => f.SortOrder)
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Folder>> GetSharedFoldersAsync(Guid userId, CancellationToken ct = default)
    {
        // Chỉ lấy folder đã accept (AcceptedAt != null) và chưa hết hạn.
        // AsSplitQuery: collection ItemFolders load ở query riêng, tránh nhân hàng.
        return await Db.FolderShares
            .AsNoTracking()
            .Include(fs => fs.Folder)
                .ThenInclude(f => f.Owner)
            .Include(fs => fs.Folder)
                .ThenInclude(f => f.ItemFolders)
            .Where(fs => fs.SharedWithUserId == userId
                         && fs.AcceptedAt != null
                         && !fs.Folder.IsArchived
                         && (fs.ExpiresAt == null || fs.ExpiresAt > DateTime.UtcNow))
            .Select(fs => fs.Folder)
            .OrderBy(f => f.SortOrder)
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<Folder?> GetByIdWithOwnerAsync(Guid folderId, CancellationToken ct = default)
    {
        return await Set
            .Include(f => f.Owner)
            .FirstOrDefaultAsync(f => f.Id == folderId, ct);
    }

    /// <inheritdoc/>
    public async Task<int> GetMaxSortOrderAsync(Guid userId, CancellationToken ct = default)
    {
        var hasFolders = await Set.AnyAsync(f => f.OwnerId == userId, ct);
        if (!hasFolders) return 0;

        return await Set
            .Where(f => f.OwnerId == userId)
            .MaxAsync(f => f.SortOrder, ct);
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsByOwnerAsync(Guid folderId, Guid userId, CancellationToken ct = default)
    {
        return await Set.AnyAsync(f => f.Id == folderId && f.OwnerId == userId, ct);
    }

    /// <inheritdoc/>
    public async Task<bool> ItemFolderExistsAsync(Guid itemId, Guid folderId, CancellationToken ct = default)
    {
        return await Db.ItemFolders.AnyAsync(ifj => ifj.ItemId == itemId && ifj.FolderId == folderId, ct);
    }

    /// <inheritdoc/>
    public async Task<int> GetMaxItemPositionAsync(Guid folderId, CancellationToken ct = default)
    {
        var hasItems = await Db.ItemFolders.AnyAsync(ifj => ifj.FolderId == folderId, ct);
        if (!hasItems) return 0;
        return await Db.ItemFolders.Where(ifj => ifj.FolderId == folderId).MaxAsync(ifj => ifj.Position, ct);
    }

    /// <inheritdoc/>
    public async Task<ItemFolder?> GetItemFolderAsync(Guid itemId, Guid folderId, CancellationToken ct = default)
    {
        return await Db.ItemFolders.FirstOrDefaultAsync(ifj => ifj.ItemId == itemId && ifj.FolderId == folderId, ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ItemFolder>> GetItemFoldersAsync(IEnumerable<Guid> itemIds, Guid folderId, CancellationToken ct = default)
    {
        return await Db.ItemFolders.Where(ifj => ifj.FolderId == folderId && itemIds.Contains(ifj.ItemId)).ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task AddItemFolderAsync(ItemFolder itemFolder, CancellationToken ct = default)
    {
        await Db.ItemFolders.AddAsync(itemFolder, ct);
    }

    /// <inheritdoc/>
    public async Task AddItemsFolderAsync(IEnumerable<ItemFolder> itemFolders, CancellationToken ct = default)
    {
        await Db.ItemFolders.AddRangeAsync(itemFolders, ct);
    }

    /// <inheritdoc/>
    public void RemoveItemFolder(ItemFolder itemFolder)
    {
        Db.ItemFolders.Remove(itemFolder);
    }

    /// <inheritdoc/>
    public void RemoveItemsFolder(IEnumerable<ItemFolder> itemFolders)
    {
        Db.ItemFolders.RemoveRange(itemFolders);
    }

    // ───── Share operations ─────

    /// <inheritdoc/>
    public async Task<FolderShare?> GetShareByIdAsync(Guid shareId, CancellationToken ct = default)
        => await Db.FolderShares
            .Include(fs => fs.Folder)
                .ThenInclude(f => f.Owner)
            .Include(fs => fs.SharedWithUser)
            .FirstOrDefaultAsync(fs => fs.Id == shareId, ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FolderShare>> GetSharesByFolderAsync(Guid folderId, CancellationToken ct = default)
        => await Db.FolderShares
            .AsNoTracking()
            .Include(fs => fs.SharedWithUser)
            .Include(fs => fs.Folder)
            .Where(fs => fs.FolderId == folderId)
            .OrderBy(fs => fs.CreatedAt)
            .ToListAsync(ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FolderShare>> GetSharesForUserAsync(Guid userId, CancellationToken ct = default)
        => await Db.FolderShares
            .AsNoTracking()
            .Include(fs => fs.Folder)
                .ThenInclude(f => f.Owner)
            .Where(fs => fs.SharedWithUserId == userId
                         && !fs.Folder.IsArchived
                         && (fs.ExpiresAt == null || fs.ExpiresAt > DateTime.UtcNow))
            .OrderByDescending(fs => fs.AcceptedAt ?? fs.CreatedAt)
            .ToListAsync(ct);

    /// <inheritdoc/>
    public async Task<bool> ShareExistsAsync(Guid folderId, Guid userId, CancellationToken ct = default)
        => await Db.FolderShares.AnyAsync(fs => fs.FolderId == folderId && fs.SharedWithUserId == userId, ct);

    /// <inheritdoc/>
    public async Task<FolderShare?> GetShareByFolderAndUserAsync(Guid folderId, Guid userId, CancellationToken ct = default)
        => await Db.FolderShares
            .Include(fs => fs.Folder)
            .Include(fs => fs.SharedWithUser)
            .FirstOrDefaultAsync(fs => fs.FolderId == folderId && fs.SharedWithUserId == userId, ct);

    /// <inheritdoc/>
    public async Task AddShareAsync(FolderShare share, CancellationToken ct = default)
        => await Db.FolderShares.AddAsync(share, ct);

    /// <inheritdoc/>
    public void RemoveShare(FolderShare share)
        => Db.FolderShares.Remove(share);

    /// <inheritdoc/>
    public async Task<bool> IsItemSharedWithUserAsync(Guid itemId, Guid userId, CancellationToken ct = default)
    {
        return await Db.ItemFolders
            .AnyAsync(ifj => ifj.ItemId == itemId &&
                             ifj.Folder.FolderShares.Any(fs => fs.SharedWithUserId == userId && fs.AcceptedAt != null), ct);
    }

    /// <inheritdoc/>
    public async Task<bool> IsItemSharedWithUserAsEditorAsync(Guid itemId, Guid userId, CancellationToken ct = default)
    {
        return await Db.ItemFolders
            .AnyAsync(ifj => ifj.ItemId == itemId &&
                             ifj.Folder.FolderShares.Any(fs => fs.SharedWithUserId == userId &&
                                                               fs.AcceptedAt != null &&
                                                               fs.Permission == SharePermission.Editor), ct);
    }

    /// <inheritdoc/>
    public async Task<bool> IsConnectionSharedWithUserAsEditorAsync(Guid connectionId, Guid userId, CancellationToken ct = default)
    {
        return await Db.ItemFolders
            .AnyAsync(ifj => ifj.Item.ConnectionId == connectionId &&
                             ifj.Folder.FolderShares.Any(fs => fs.SharedWithUserId == userId &&
                                                               fs.AcceptedAt != null &&
                                                               fs.Permission == SharePermission.Editor), ct);
    }
}
