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
        int page = 1,
        int limit = 20,
        CancellationToken ct = default);

    Task<HashSet<string>> GetExistingExternalIdsAsync(Guid connectionId, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<Item> items, CancellationToken ct = default);

    /// <summary>
    /// Tìm Item theo ID và User, dùng để check ownership trước khi update/delete.
    /// </summary>
    Task<Item?> GetByIdAndUserAsync(Guid itemId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Set ConnectionId = NULL cho tất cả Items đang trỏ vào connectionId.
    /// Dùng khi disconnect connection (SCRUM-14) — Items giữ lại nhưng mất liên kết.
    /// </summary>
    Task NullifyConnectionIdAsync(Guid connectionId, CancellationToken ct = default);
}

