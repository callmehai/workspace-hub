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
    Task<(IReadOnlyList<Item> Items, int TotalCount)> GetPagedAsync(
        Guid userId,
        Guid? folderId = null,
        ItemStatus? status = null,
        ItemType? type = null,
        bool? isImportant = null,
        string? search = null,
        Guid? tagId = null,
        int page = 1,
        int limit = 20,
        CancellationToken ct = default);

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
    /// Tìm danh sách Item theo IDs và User, dùng để check ownership trong bulk operations.
    /// </summary>
    Task<List<Item>> GetByIdsAndUserAsync(IEnumerable<Guid> itemIds, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Xóa toàn bộ Items và các liên kết (ItemFolders, TagAssignments) thuộc connectionId.
    /// Dùng khi disconnect connection để tránh vi phạm Unique Index (ConnectionId, ExternalId) do ConnectionId=NULL trùng lặp.
    /// </summary>
    Task DeleteByConnectionIdAsync(Guid connectionId, CancellationToken ct = default);
}

