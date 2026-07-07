namespace WorkspaceHub.Application.DTOs;

// ───────────────────────── Request DTOs ─────────────────────────

/// <summary>POST /api/tags — tạo tag mới (SCRUM-70). Name KHÔNG unique toàn hệ thống, nhưng unique trong 1 user.</summary>
public record CreateTagRequest(
    string Name,
    string Color);

/// <summary>PUT /api/tags/{id} — đổi tên / màu tag.</summary>
public record UpdateTagRequest(
    string Name,
    string Color);

/// <summary>POST /api/tags/{id}/items — gắn tag vào item.</summary>
public record AssignTagRequest(Guid ItemId);

// ───────────────────────── Response DTOs ─────────────────────────

/// <summary>Kết quả cho GET /api/tags. ItemCount = số item đang gắn tag này.</summary>
public record TagResponse(
    Guid Id,
    string Name,
    string Color,
    int ItemCount);

/// <summary>Response cho thao tác gắn tag ↔ item.</summary>
public record TagAssignmentResponse(
    Guid TagId,
    Guid ItemId,
    DateTime AssignedAt);
