using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.DTOs;

// ───────────────────────── Query Request ─────────────────────────

/// <summary>
/// GET /api/items — query parameters cho filtering, searching, pagination.
/// API.md: "?folderId=&status=&type=&isImportant=&search=&page=&limit="
/// </summary>
public record GetItemsRequest(
    Guid? FolderId = null,
    ItemStatus? Status = null,
    ItemType? Type = null,
    bool? IsImportant = null,
    string? Search = null,
    int Page = 1,
    int Limit = 20);

// ───────────────────────── Command Request ─────────────────────────

/// <summary>
/// PATCH /api/items/{id}/status — body chỉ có status mới
/// </summary>
public record UpdateItemStatusRequest(ItemStatus Status);

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
    string? Comment = null              // thêm comment (thao tác riêng, không sửa field)
);

public record CreateEventRequest(
    Guid ConnectionId,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Location = null,
    List<string>? Attendees = null
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
    string? MetadataJson);


