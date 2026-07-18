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
    public async Task<(IReadOnlyList<Item> Items, int TotalCount, IReadOnlyDictionary<string, int> ThreadCounts)> GetPagedAsync(
        Guid userId,
        Guid? folderId = null,
        IReadOnlyList<ItemStatus>? statuses = null,
        IReadOnlyList<ItemType>? types = null,
        bool? isImportant = null,
        string? search = null,
        IReadOnlyList<Guid>? tagIds = null,
        string? projectKey = null,
        string? gmailLabel = null,
        string? assigneeAccountId = null,
        Guid? connectionId = null,
        DateTime? occurredFrom = null,
        DateTime? occurredTo = null,
        string? driveParentId = null,
        string? driveKind = null,
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

        // FolderId: join qua ItemFolders junction table.
        //
        // NGOẠI LỆ: khi đang DUYỆT VÀO TRONG một thư mục Drive cụ thể (driveParentId có giá trị và
        // khác "root"), bỏ qua filter workspace-folder. Lý do: user chỉ gán THƯ MỤC Drive vào
        // workspace folder, các file con bên trong KHÔNG được gán → AND hai filter cho ra tập rỗng
        // (thư mục Drive mở ra trống trơn dù All items vẫn thấy file). Ngữ cảnh lúc này là cây Drive,
        // không phải workspace folder.
        var browsingDriveFolder = driveParentId != null && driveParentId != "root";
        if (folderId.HasValue && !browsingDriveFolder)
        {
            query = query.Where(i =>
                i.ItemFolders.Any(ifj => ifj.FolderId == folderId.Value));
        }

        // TagIds: join qua TagAssignments junction table — đa chọn theo OR
        // (item khớp nếu mang BẤT KỲ tag nào trong danh sách đã chọn).
        if (tagIds is { Count: > 0 })
        {
            query = query.Where(i =>
                i.TagAssignments.Any(ta => tagIds.Contains(ta.TagId)));
        }

        // Status: Kanban column filter (Inbox/Doing/Done) — đa chọn
        if (statuses is { Count: > 0 })
        {
            query = query.Where(i => statuses.Contains(i.Status));
        }

        // Type: Email/Event/File/Note — đa chọn
        if (types is { Count: > 0 })
        {
            query = query.Where(i => types.Contains(i.Type));
        }

        // IsImportant flag
        if (isImportant.HasValue)
        {
            query = query.Where(i => i.IsImportant == isImportant.Value);
        }

        // Gmail label filter (Email): metadata.labels là JSON array, vd ["INBOX","UNREAD",...].
        // Match token đã bọc ngoặc kép để không dính substring nhầm (INBOX/SENT/DRAFT/STARRED/
        // IMPORTANT/CATEGORY_*). Chỉ Email mới có labels nên loại khác tự loại khỏi kết quả.
        if (!string.IsNullOrWhiteSpace(gmailLabel))
        {
            var lbl = $"\"{gmailLabel.Trim()}\"";
            query = query.Where(i => i.MetadataJson != null && i.MetadataJson.Contains(lbl));
            
            if (gmailLabel.Trim() != "TRASH" && gmailLabel.Trim() != "SPAM")
            {
                query = query.Where(i => i.MetadataJson == null ||
                    (!i.MetadataJson.Contains("\"SPAM\"") && !i.MetadataJson.Contains("\"TRASH\"")));
            }
        }
        else
        {
            // Không lọc mailbox cụ thể → loại Spam/Trash khỏi các view tổng (giống Gmail: "Tất cả thư"
            // KHÔNG gồm Spam/Trash). Item không có labels (Event/File/Note/Ticket) không bị ảnh hưởng.
            query = query.Where(i => i.MetadataJson == null ||
                (!i.MetadataJson.Contains("\"SPAM\"") && !i.MetadataJson.Contains("\"TRASH\"")));
        }

        // Project filter (for Jira tickets)
        if (!string.IsNullOrWhiteSpace(projectKey))
        {
            var pk = projectKey.Trim();
            // Fallback for simple JSON search since EF.Functions.JsonValue may not be mapped
            query = query.Where(i => i.MetadataJson != null && i.MetadataJson.Contains($"\"projectKey\":\"{pk}\""));
        }

        // Assignee filter (Jira tickets) — "unassigned" = ticket chưa gán (assigneeAccountId null).
        if (!string.IsNullOrWhiteSpace(assigneeAccountId))
        {
            var needle = assigneeAccountId.Trim() == "unassigned"
                ? "\"assigneeAccountId\":null"
                : $"\"assigneeAccountId\":\"{assigneeAccountId.Trim()}\"";
            query = query.Where(i => i.MetadataJson != null && i.MetadataJson.Contains(needle));
        }

        // Connection filter — lọc item thuộc 1 connection cụ thể (Drive modal parent dropdown, v.v.)
        if (connectionId.HasValue)
        {
            query = query.Where(i => i.ConnectionId == connectionId.Value);
        }

        // Calendar range overlap: start < rangeEnd AND end > rangeStart (OccurredAt=start, DueAt=end).
        // Ticket chỉ hiện trên lịch khi có deadline (DueAt); không dùng OccurredAt=updated làm mốc lịch.
        if (occurredFrom.HasValue || occurredTo.HasValue)
        {
            var rangeStart = occurredFrom ?? DateTime.MinValue;
            var rangeEnd = occurredTo ?? DateTime.MaxValue;
            query = query.Where(i =>
                (i.Type != ItemType.Ticket || i.DueAt.HasValue) &&
                i.OccurredAt < rangeEnd &&
                (i.DueAt ?? i.OccurredAt) > rangeStart);
        }

        // DriveParentId filter — hierarchical Google Drive view (SCRUM-79 extension)
        if (browsingDriveFolder)
        {
            // Thư mục cụ thể: CHỈ hiển thị các file/folder Drive nằm trong thư mục này.
            // `!`: browsingDriveFolder đã bao hàm driveParentId != null, nhưng flow analysis
            // không xuyên qua được biến bool trung gian.
            var parentToken = $"\"{driveParentId!.Trim()}\"";
            query = query.Where(i => i.Type == ItemType.File && i.MetadataJson != null && i.MetadataJson.Contains(parentToken));
        }
        else if (driveParentId == "root" || (folderId == null && driveParentId == null && string.IsNullOrEmpty(search)))
        {
            // Root (All items): hiển thị item Drive NẾU nó được đánh dấu là TopLevel (đã tính toán trong DriveSyncService)
            // Bỏ qua filter này nếu đang browse Workspace Folder (folderId != null) hoặc đang Search.
            query = query.Where(i => i.Type != ItemType.File || (i.MetadataJson != null && i.MetadataJson.Contains("\"isTopLevel\":true")));
        }

        // DriveKind filter — chỉ Thư mục / chỉ Tệp trong view Drive. FE chỉ gửi param này ở ngữ cảnh Drive.
        // "folder" = metadata có "isFolder":true. "file" = MỌI thứ còn lại (NOT folder) — định nghĩa theo
        // phần bù để item cũ / metadata hỏng thiếu hẳn field "isFolder" vẫn được coi là tệp (không bị giấu
        // khỏi tab "Tệp"), thay vì đòi khớp cứng "isFolder":false.
        if (!string.IsNullOrWhiteSpace(driveKind))
        {
            var kind = driveKind.Trim().ToLowerInvariant();
            if (kind == "folder")
                query = query.Where(i => i.MetadataJson != null && i.MetadataJson.Contains("\"isFolder\":true"));
            else if (kind == "file")
                query = query.Where(i => i.MetadataJson == null || !i.MetadataJson.Contains("\"isFolder\":true"));
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

        // ── Gộp thread (chỉ Email có ThreadId) ──
        // Mỗi thread chỉ giữ message MỚI NHẤT (OccurredAt lớn nhất; tie-break ExternalId)
        // trong tập đã lọc. Item không có ThreadId (non-Email / chưa có) giữ nguyên từng dòng.
        var filtered = query;
        var deduped = filtered.Where(i =>
            i.ThreadId == null ||
            !filtered.Any(o =>
                o.ThreadId == i.ThreadId &&
                (o.OccurredAt > i.OccurredAt ||
                 (o.OccurredAt == i.OccurredAt && string.Compare(o.ExternalId, i.ExternalId) > 0))));

        // ── Count total (sau khi gộp thread, trước khi paging) ──
        var totalCount = await deduped.CountAsync(ct);

        // ── Sort + Paging (DB level) ──
        // Trong ngữ cảnh Drive (đang duyệt trong 1 folder, hoặc tab chỉ-File) → đẩy FOLDER lên trước
        // FILE (giống mọi trình quản lý file), rồi mới tới mới-nhất. View khác (Email/All) giữ nguyên
        // sort theo thời gian — điều kiện dưới chỉ đúng ở view Drive nên không đụng các tab kia.
        var foldersFirst = driveParentId != null
            || (types is { Count: 1 } && types[0] == ItemType.File);

        var ordered = foldersFirst
            ? deduped
                .OrderByDescending(i => i.MetadataJson != null && i.MetadataJson.Contains("\"isFolder\":true"))
                .ThenByDescending(i => i.OccurredAt)
            : deduped.OrderByDescending(i => i.OccurredAt);

        var items = await ordered
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(ct);

        // ── Số message thật của mỗi thread xuất hiện trong trang (đếm toàn bộ của user,
        //    không phụ thuộc filter — giống Gmail hiển thị tổng số thư trong thread). ──
        var pageThreadIds = items.Where(i => i.ThreadId != null).Select(i => i.ThreadId!).Distinct().ToList();
        var threadCounts = pageThreadIds.Count == 0
            ? new Dictionary<string, int>()
            : await Set.AsNoTracking()
                .Where(i => i.UserId == userId && !i.IsArchived && i.ThreadId != null && pageThreadIds.Contains(i.ThreadId))
                .GroupBy(i => i.ThreadId!)
                .Select(g => new { ThreadId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ThreadId, x => x.Count, ct);

        return (items.AsReadOnly(), totalCount, threadCounts);
    }

    public async Task<IReadOnlyList<(string? AccountId, string DisplayName)>> GetTicketAssigneesAsync(Guid userId, CancellationToken ct = default)
    {
        // Lấy JSON metadata của mọi Ticket của user rồi trích assignee ở memory (board vài trăm item — rẻ).
        var jsons = await Set.AsNoTracking()
            .Where(i => i.UserId == userId && i.Type == ItemType.Ticket && !i.IsArchived && i.MetadataJson != null)
            .Select(i => i.MetadataJson!)
            .ToListAsync(ct);

        var byId = new Dictionary<string, string>();   // accountId → displayName
        var hasUnassigned = false;

        foreach (var json in jsons)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                var accId = root.TryGetProperty("assigneeAccountId", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.String
                    ? a.GetString()
                    : null;
                var name = root.TryGetProperty("assignee", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String
                    ? n.GetString()
                    : null;

                if (string.IsNullOrEmpty(accId)) { hasUnassigned = true; continue; }
                byId[accId] = string.IsNullOrWhiteSpace(name) ? accId : name!;
            }
            catch (System.Text.Json.JsonException) { /* metadata hỏng → bỏ qua */ }
        }

        var result = byId
            .Select(kv => ((string?)kv.Key, kv.Value))
            .OrderBy(x => x.Value, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (hasUnassigned)
            result.Add(("unassigned", "Chưa gán"));

        return result;
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
            .Include(i => i.Reminders)
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
            .Include(i => i.Reminders)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == userId, ct);
    }

    /// <inheritdoc/>
    public async Task<Item?> GetByConnectionAndExternalIdAsync(
        Guid userId,
        Guid connectionId,
        string externalId,
        CancellationToken ct = default)
    {
        return await Set.FirstOrDefaultAsync(
            i => i.UserId == userId
                 && i.ConnectionId == connectionId
                 && i.ExternalId == externalId,
            ct);
    }

    /// <inheritdoc/>
    public async Task<List<Item>> GetByIdsAndUserAsync(IEnumerable<Guid> itemIds, Guid userId, CancellationToken ct = default)
    {
        return await Set.Where(i => itemIds.Contains(i.Id) && i.UserId == userId).ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<List<Item>> GetFilesByExternalIdsAsync(Guid userId, IEnumerable<string> externalIds, CancellationToken ct = default)
    {
        var ids = externalIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        if (ids.Count == 0)
            return [];

        return await Set
            .Where(i => i.UserId == userId
                && i.Type == ItemType.File
                && i.ExternalId != null
                && ids.Contains(i.ExternalId))
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<List<Item>> GetDriveItemsByConnectionsAsync(Guid userId, IReadOnlyList<Guid> connectionIds, CancellationToken ct = default)
    {
        if (connectionIds.Count == 0)
            return [];

        return await Set
            .Where(i => i.UserId == userId
                && i.Type == ItemType.File
                && !i.IsArchived
                && i.ConnectionId != null
                && connectionIds.Contains(i.ConnectionId.Value))
            .ToListAsync(ct);
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

        // 3. Null InviteeItemId trước khi ExecuteDelete Items (FK NoAction — SQL Server không cho
        // SET NULL khi OrganizerItemId đã CASCADE cùng trỏ Items). OrganizerItem CASCADE tự xoá invitation.
        var now = DateTime.UtcNow;
        await Db.CalendarInvitations
            .Where(ci => ci.InviteeItem != null && ci.InviteeItem.ConnectionId == connectionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(ci => ci.InviteeItemId, (Guid?)null)
                .SetProperty(ci => ci.UpdatedAt, now), ct);

        // 4. Delete the Items themselves
        await Set
            .Where(i => i.ConnectionId == connectionId)
            .ExecuteDeleteAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<int> DeleteThreadAsync(Guid userId, string threadId, CancellationToken ct = default)
    {
        // 1. ItemFolders của mọi item trong thread
        await Db.ItemFolders
            .Where(x => x.Item.UserId == userId && x.Item.ThreadId == threadId)
            .ExecuteDeleteAsync(ct);

        // 2. TagAssignments của mọi item trong thread
        await Db.TagAssignments
            .Where(x => x.Item.UserId == userId && x.Item.ThreadId == threadId)
            .ExecuteDeleteAsync(ct);

        // 3. Bản thân các Item cùng ThreadId
        return await Set
            .Where(i => i.UserId == userId && i.ThreadId == threadId)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Escape ký tự wildcard của SQL LIKE (%, _, [) để chuỗi search được so theo nghĩa đen.
    /// EF.Functions.Like KHÔNG tự escape như string.Contains.
    /// </summary>
    private static string EscapeLikePattern(string input) =>
        input.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    /// <inheritdoc/>
    public async Task<Item?> GetByThreadAndConnectionAsync(string threadId, Guid connectionId, CancellationToken ct = default)
    {
        return await Set.AsNoTracking()
            .FirstOrDefaultAsync(i => i.ThreadId == threadId && i.ConnectionId == connectionId, ct);
    }
}
