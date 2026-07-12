using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.DTOs;

// ───────────────────────── Query Request ─────────────────────────

/// <summary>
/// GET /api/items — query parameters cho filtering, searching, pagination.
/// API.md: "?folderId=&status=&type=&isImportant=&search=&page=&limit="
/// </summary>
public record GetItemsRequest(
    Guid? FolderId = null,
    IReadOnlyList<ItemStatus>? Statuses = null,
    IReadOnlyList<ItemType>? Types = null,
    bool? IsImportant = null,
    string? Search = null,
    IReadOnlyList<Guid>? TagIds = null,   // đa chọn: item khớp nếu mang BẤT KỲ tag nào trong danh sách (OR)
    string? ProjectKey = null,
    string? GmailLabel = null,
    string? Assignee = null,      // Jira accountId; "unassigned" = ticket chưa gán người
    Guid? ConnectionId = null,    // Lọc item theo connection (Drive/Gmail/…)
    DateTime? OccurredFrom = null, // UTC inclusive — overlap filter (Event calendar range)
    DateTime? OccurredTo = null,   // UTC exclusive
    int Page = 1,
    int Limit = 20);

// ───────────────────────── Command Request ─────────────────────────

/// <summary>
/// PATCH /api/items/{id}/status — body chỉ có status mới
/// </summary>
public record UpdateItemStatusRequest(ItemStatus Status);
public record UpdateItemImportantRequest(bool IsImportant);

/// <summary>
/// POST /api/items/note — tạo Note mới
/// </summary>
public record CreateNoteRequest(
    string Title,
    string ContentMarkdown,
    Guid? FolderId = null);

public record PatchItemRequest(
    bool? IsUnread = null,
    bool? IsStarred = null,
    List<string>? AddLabels = null,
    List<string>? RemoveLabels = null,
    bool? IsTrashed = null,
    string? Title = null,
    DateTimeOffset? Start = null,
    DateTimeOffset? End = null,
    string? Location = null,
    List<string>? Attendees = null,
    string? Name = null,
    // ── Jira (Type=Ticket) — SCRUM-57. Nội dung Jira SỬA ĐƯỢC (khác Email immutable).
    string? Summary = null,
    string? Description = null,
    string? Assignee = null,            // accountId
    string? Priority = null,            // tên priority
    string? StatusTransition = null,    // id hoặc tên transition (đổi status qua transition)
    List<string>? Labels = null,        // set toàn bộ labels (thay vì add/remove)
    string? Comment = null,             // thêm comment (thao tác riêng, không sửa field)
    string? IssueType = null,           // đổi loại issue (Task/Bug/Story...) qua PUT /issue
    // ── Google Calendar (Type=Event) — SCRUM-37
    bool? AllDay = null,
    List<Guid>? DriveItemIds = null     // danh sách file đính kèm từ Drive
);

public record CreateEventRequest(
    Guid ConnectionId,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,                 // bắt buộc; all-day: FE gửi ngày kế tiếp
    string? Location = null,
    List<string>? Attendees = null,
    string? Description = null,
    bool AllDay = false,
    List<Guid>? DriveItemIds = null     // ID Item Drive trong DB — BE resolve ra fileId/title/mimeType
);

/// <summary>
/// POST /api/items/ticket — tạo issue Jira mới (SCRUM-56).
/// connectionId phải là Connection ServiceType=Jira. assignee = accountId; description = plain text (service → ADF).
/// </summary>
public record CreateTicketRequest(
    Guid ConnectionId,
    string ProjectKey,
    string IssueType,
    string Summary,
    string? Description = null,
    string? Assignee = null,
    string? Priority = null,
    List<string>? Labels = null
);

/// <summary>1 người phụ trách (assignee) cho filter Jira. AccountId = "unassigned" khi ticket chưa gán.</summary>
public record JiraAssigneeDto(string AccountId, string DisplayName);

/// <summary>1 comment của ticket Jira (trả về FE).</summary>
public record JiraCommentDto(string Id, string Body, string AuthorName, string? AuthorAccountId, DateTimeOffset? Created, DateTimeOffset? Updated);

/// <summary>Body tạo/sửa comment. Body = markdown subset. MediaIds = attachment id nhúng vào comment (chỉ dùng khi tạo).</summary>
public record TicketCommentBody(string Body, List<string>? MediaIds = null);

/// <summary>1 attachment của ticket Jira (metadata trả về FE).</summary>
public record JiraAttachmentDto(string Id, string Filename, string? MimeType, long Size, string? AuthorName, DateTimeOffset? Created);

// ───────────────────────── Response DTO ─────────────────────────

/// <summary>
/// Single item trong list response.
/// Chỉ chứa metadata cần thiết cho danh sách — body/detail lấy qua GET /api/items/{id}/detail.
/// </summary>
public record ItemResponse(
    Guid Id,
    ItemType Type,
    string Title,
    string Snippet,
    ItemStatus Status,
    DateTime OccurredAt,
    DateTime? DueAt,
    bool IsImportant,
    string? ExternalId,
    string? MetadataJson,
    List<Guid> FolderIds,
    List<ItemTag> Tags,
    Guid? ConnectionId = null,
    string? ThreadId = null,
    int ThreadCount = 1);

/// <summary>Tag đang gắn vào item (rút gọn để nhúng trong ItemResponse — SCRUM-71).</summary>
public record ItemTag(
    Guid Id,
    string Name,
    string Color);


