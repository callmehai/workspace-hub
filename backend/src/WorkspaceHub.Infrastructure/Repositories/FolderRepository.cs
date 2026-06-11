using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation cho <see cref="IFolderRepository"/>.
/// AsNoTracking cho read-only queries. Include/projection tránh N+1.
/// </summary>
public class FolderRepository : GenericRepository<Folder>, IFolderRepository
{
    public FolderRepository(AppDbContext db) : base(db) { }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Folder>> GetUserFoldersAsync(Guid userId, CancellationToken ct = default)
    {
        return await Set
            .AsNoTracking()
            .Include(f => f.Owner)
            .Include(f => f.ItemFolders)
            .Include(f => f.FolderShares)
            .Where(f => f.OwnerId == userId && !f.IsArchived)
            .OrderBy(f => f.SortOrder)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Folder>> GetSharedFoldersAsync(Guid userId, CancellationToken ct = default)
    {
        // Chỉ lấy folder đã accept (AcceptedAt != null) và chưa hết hạn.
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
}
