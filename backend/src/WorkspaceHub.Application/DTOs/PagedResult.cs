namespace WorkspaceHub.Application.DTOs;

/// <summary>
/// Generic envelope cho paginated response.
/// API.md: "List lớn (items, users, notifications, scheduled-emails) → envelope { items, total, page, limit }"
/// Reusable cho mọi endpoint cần pagination.
/// </summary>
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Page,
    int Limit);
