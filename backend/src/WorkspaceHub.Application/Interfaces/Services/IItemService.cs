using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// Business logic cho Items.
/// Controller gọi service → service gọi repository → trả DTO.
/// </summary>
public interface IItemService
{
    /// <summary>
    /// Lấy danh sách Items phân trang cho user, áp dụng filter + search.
    /// Trả envelope { items, total, page, limit } theo API.md.
    /// </summary>
    Task<PagedResult<ItemResponse>> GetItemsAsync(
        Guid userId, GetItemsRequest request, CancellationToken ct = default);

    /// <summary>
    /// Cập nhật trạng thái Kanban cho Item.
    /// </summary>
    Task<ItemResponse> UpdateStatusAsync(
        Guid userId, Guid itemId, UpdateItemStatusRequest request, CancellationToken ct = default);

    /// <summary>
    /// Tạo ghi chú nội bộ (Note) mới.
    /// </summary>
    Task<ItemResponse> CreateNoteAsync(
        Guid userId, CreateNoteRequest request, CancellationToken ct = default);

    /// <summary>
    /// Lấy chi tiết một Item theo ID.
    /// </summary>
    Task<ItemResponse> GetItemByIdAsync(Guid userId, Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// Cập nhật cờ quan trọng (IsImportant) của Item.
    /// </summary>
    Task<ItemResponse> ToggleImportantAsync(Guid userId, Guid itemId, bool isImportant, CancellationToken ct = default);
}
