using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation cho IItemRepository.
/// Query phân trang + filter + search thực hiện hoàn toàn ở DB level.
/// Sử dụng composite index IX_Items_User_Status_OccurredAt cho performance.
/// </summary>
public class ItemRepository : GenericRepository<Item>, IItemRepository
{
    public ItemRepository(AppDbContext db) : base(db) { }

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<Item> Items, int TotalCount)> GetPagedAsync(
        Guid userId,
        Guid? folderId = null,
        ItemStatus? status = null,
        ItemType? type = null,
        bool? isImportant = null,
        string? search = null,
        int page = 1,
        int limit = 20,
        CancellationToken ct = default)
    {
        // Base query: items thuộc user, chưa archived, AsNoTracking cho read-only
        var query = Set.AsNoTracking()
            .Where(i => i.UserId == userId && !i.IsArchived);

        // ── Optional filters ──

        // FolderId: join qua ItemFolders junction table
        if (folderId.HasValue)
        {
            query = query.Where(i =>
                i.ItemFolders.Any(ifj => ifj.FolderId == folderId.Value));
        }

        // Status: Kanban column filter (Inbox/Doing/Done)
        if (status.HasValue)
        {
            query = query.Where(i => i.Status == status.Value);
        }

        // Type: Email/Event/File/Note
        if (type.HasValue)
        {
            query = query.Where(i => i.Type == type.Value);
        }

        // IsImportant flag
        if (isImportant.HasValue)
        {
            query = query.Where(i => i.IsImportant == isImportant.Value);
        }

        // ── Search: Title hoặc Snippet ──
        // SQL Server dùng collation CI (Case-Insensitive) mặc định,
        // nên LIKE '%...%' đã case-insensitive sẵn, KHÔNG cần .ToLower()
        // (dùng ToLower sẽ sinh LOWER() trong SQL → ngăn sử dụng index).
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(i =>
                i.Title.Contains(search) ||
                i.Snippet.Contains(search));
        }

        // ── Count total (trước khi paging) ──
        var totalCount = await query.CountAsync(ct);

        // ── Sort + Paging (DB level) ──
        var items = await query
            .OrderByDescending(i => i.OccurredAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(ct);

        return (items.AsReadOnly(), totalCount);
    }

    public async Task<HashSet<string>> GetExistingExternalIdsAsync(Guid connectionId, CancellationToken ct = default)
    {
        var ids = await Set.AsNoTracking()
            .Where(i => i.ConnectionId == connectionId && i.ExternalId != null)
            .Select(i => i.ExternalId!)
            .ToListAsync(ct);
            
        return new HashSet<string>(ids);
    }

    public async Task AddRangeAsync(IEnumerable<Item> items, CancellationToken ct = default)
    {
        await Set.AddRangeAsync(items, ct);
    }
}
