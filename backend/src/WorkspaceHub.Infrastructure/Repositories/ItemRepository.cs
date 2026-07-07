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
        Guid? tagId = null,
        int page = 1,
        int limit = 20,
        CancellationToken ct = default)
    {
        // Base query: items thuộc user, chưa archived, AsNoTracking cho read-only
        var query = Set.AsNoTracking()
            .Include(i => i.ItemFolders)
            .Include(i => i.TagAssignments)
                .ThenInclude(ta => ta.Tag)
            .Where(i => i.UserId == userId && !i.IsArchived);

        // ── Optional filters ──

        // FolderId: join qua ItemFolders junction table
        if (folderId.HasValue)
        {
            query = query.Where(i =>
                i.ItemFolders.Any(ifj => ifj.FolderId == folderId.Value));
        }

        // TagId: join qua TagAssignments junction table
        if (tagId.HasValue)
        {
            query = query.Where(i =>
                i.TagAssignments.Any(ta => ta.TagId == tagId.Value));
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
        // Ép collation Latin1_General_100_CI_AI ngay trong predicate:
        //   CI = case-insensitive, AI = ACCENT-insensitive → gõ "bao gia" khớp "Báo giá",
        //   "đ" khớp "d" (không bắt user gõ đúng dấu tiếng Việt 100%).
        // LIKE '%...%' vốn đã không dùng được index nên Collate không làm chậm thêm.
        // Escape ký tự wildcard của LIKE (%, _, [) để search theo nghĩa đen.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLikePattern(search.Trim())}%";
            query = query.Where(i =>
                EF.Functions.Like(EF.Functions.Collate(i.Title, "Latin1_General_100_CI_AI"), pattern) ||
                EF.Functions.Like(EF.Functions.Collate(i.Snippet, "Latin1_General_100_CI_AI"), pattern));
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

    public async Task<Dictionary<string, Item>> GetTrackedByConnectionIdAsync(Guid connectionId, CancellationToken ct = default)
    {
        // Tracked (KHÔNG AsNoTracking) để cập nhật item persist khi SaveChanges.
        var items = await Set
            .Include(i => i.ItemFolders)
            .Where(i => i.ConnectionId == connectionId && i.ExternalId != null)
            .ToListAsync(ct);

        return items.ToDictionary(i => i.ExternalId!, i => i);
    }

    public async Task AddRangeAsync(IEnumerable<Item> items, CancellationToken ct = default)
    {
        await Set.AddRangeAsync(items, ct);
    }

    /// <inheritdoc/>
    public async Task<Item?> GetByIdAndUserAsync(Guid itemId, Guid userId, CancellationToken ct = default)
    {
        return await Set
            .Include(i => i.ItemFolders)
            .Include(i => i.TagAssignments)
                .ThenInclude(ta => ta.Tag)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == userId, ct);
    }

    /// <inheritdoc/>
    public async Task<List<Item>> GetByIdsAndUserAsync(IEnumerable<Guid> itemIds, Guid userId, CancellationToken ct = default)
    {
        return await Set.Where(i => itemIds.Contains(i.Id) && i.UserId == userId).ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task DeleteByConnectionIdAsync(Guid connectionId, CancellationToken ct = default)
    {
        // 1. Delete associated ItemFolders
        await Db.ItemFolders
            .Where(x => x.Item.ConnectionId == connectionId)
            .ExecuteDeleteAsync(ct);

        // 2. Delete associated TagAssignments
        await Db.TagAssignments
            .Where(x => x.Item.ConnectionId == connectionId)
            .ExecuteDeleteAsync(ct);

        // 3. Delete the Items themselves
        await Set
            .Where(i => i.ConnectionId == connectionId)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Escape ký tự wildcard của SQL LIKE (%, _, [) để chuỗi search được so theo nghĩa đen.
    /// EF.Functions.Like KHÔNG tự escape như string.Contains.
    /// </summary>
    private static string EscapeLikePattern(string input) =>
        input.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
