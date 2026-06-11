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
}
