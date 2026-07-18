using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Interfaces.Repositories;

/// <summary>
/// Repository riêng cho Item — truy vấn phân trang + filter + search phức tạp hơn GenericRepository.
/// </summary>
public interface IItemRepository : IGenericRepository<Item>
{
    /// <summary>
    /// Lấy danh sách Item phân trang cho user, với filtering và search.
    /// Trả về tuple: (danh sách items trong page, tổng số items khớp filter).
    /// Pagination thực hiện ở DB level (Skip/Take).
    /// </summary>
    Task<(IReadOnlyList<Item> Items, int TotalCount, IReadOnlyDictionary<string, int> ThreadCounts)> GetPagedAsync(
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
        CancellationToken ct = default);

    /// <summary>Danh sách assignee (accountId + tên) suy từ Ticket đã sync của user — cho filter theo người.</summary>
    Task<IReadOnlyList<(string? AccountId, string DisplayName)>> GetTicketAssigneesAsync(Guid userId, CancellationToken ct = default);

    Task<HashSet<string>> GetExistingExternalIdsAsync(Guid connectionId, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<Item> items, CancellationToken ct = default);

    /// <summary>
    /// Lấy items của 1 connection dạng tracked, key theo ExternalId — để sync cập nhật item đã tồn tại
    /// (vd Jira issue đổi title/status sau khi đã sync). Bỏ qua item có ExternalId null.
    /// </summary>
    Task<Dictionary<string, Item>> GetTrackedByConnectionIdAsync(Guid connectionId, CancellationToken ct = default);

    /// <summary>
    /// Tìm Item theo ID và User, dùng để check ownership trước khi update/delete.
    /// </summary>
    Task<Item?> GetByIdAndUserAsync(Guid itemId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Tìm Item Drive theo ExternalId (Google file/folder id) trong một connection của user.
    /// Dùng khi resolve folder mẹ từ metadata.parents (Case 1 link-restrict conflict).
    /// </summary>
    Task<Item?> GetByConnectionAndExternalIdAsync(
        Guid userId,
        Guid connectionId,
        string externalId,
        CancellationToken ct = default);

    /// <summary>
    /// Tìm danh sách Item theo IDs và User, dùng để check ownership trong bulk operations.
    /// </summary>
    Task<List<Item>> GetByIdsAndUserAsync(IEnumerable<Guid> itemIds, Guid userId, CancellationToken ct = default);

    /// <summary>Drive file Items theo Google file id (ExternalId) — dùng khi sync Calendar attachment → driveItemIds.</summary>
    Task<List<Item>> GetFilesByExternalIdsAsync(Guid userId, IEnumerable<string> externalIds, CancellationToken ct = default);

    /// <summary>
    /// Xóa toàn bộ Items và các liên kết (ItemFolders, TagAssignments) thuộc connectionId.
    /// Dùng khi disconnect connection để tránh vi phạm Unique Index (ConnectionId, ExternalId) do ConnectionId=NULL trùng lặp.
    /// </summary>
    Task DeleteByConnectionIdAsync(Guid connectionId, CancellationToken ct = default);

    /// <summary>
    /// Xóa toàn bộ Items (và liên kết ItemFolders/TagAssignments) cùng ThreadId của user — dùng khi
    /// xoá 1 email gộp thread: mỗi thư trong thread là 1 row riêng nên phải xoá hết để thread biến mất.
    /// Trả về số Item row đã xoá.
    /// </summary>
    Task<int> DeleteThreadAsync(Guid userId, string threadId, CancellationToken ct = default);
}

