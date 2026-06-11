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
    string? ExternalId);


