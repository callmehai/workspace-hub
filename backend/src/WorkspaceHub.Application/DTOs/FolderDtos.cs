namespace WorkspaceHub.Application.DTOs;

// ───────────────────────── Request DTOs ─────────────────────────

/// <summary>POST /api/folders — tạo folder mới.</summary>
public record CreateFolderRequest(
    string Name,
    string Color,
    string Icon);

/// <summary>PUT /api/folders/{id} — cập nhật folder metadata.</summary>
public record UpdateFolderRequest(
    string Name,
    string Color,
    string Icon,
    int SortOrder);

// ───────────────────────── Response DTO ─────────────────────────

/// <summary>
/// Kết quả trả về cho GET /api/folders.
/// Bao gồm computed fields: ItemCount, IsOwner, Permission, OwnerName.
/// </summary>
public record FolderResponse(
    Guid Id,
    string Name,
    string Color,
    string Icon,
    int SortOrder,
    bool IsArchived,
    int ItemCount,
    bool IsOwner,
    string Permission,
    string OwnerName);
