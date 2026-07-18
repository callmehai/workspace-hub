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

    /// <summary>Danh sách người phụ trách (assignee) suy từ ticket Jira đã sync — cho filter theo user.</summary>
    Task<IReadOnlyList<JiraAssigneeDto>> GetTicketAssigneesAsync(Guid userId, CancellationToken ct = default);

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

    /// <summary>
    /// Lấy chi tiết sự kiện lịch từ Google Calendar kèm cấu hình nhắc nhở local.
    /// </summary>
    Task<CalendarEventDetailResponse> GetCalendarEventDetailAsync(Guid userId, Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// Cập nhật phản hồi RSVP của user lên Google Calendar.
    /// </summary>
    Task RsvpEventAsync(Guid userId, Guid itemId, RsvpRequest request, CancellationToken ct = default);

    /// <summary>
    /// Gửi email nội dung chi tiết sự kiện đến danh sách khách mời.
    /// </summary>
    Task SendEmailToGuestsAsync(Guid userId, Guid itemId, SendEmailToGuestsRequest request, CancellationToken ct = default);
}
