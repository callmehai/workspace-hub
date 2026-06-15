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

/// <summary>POST /api/folders/{folderId}/items — gắn item vào folder</summary>
public record AddItemToFolderRequest(Guid ItemId);

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

/// <summary>Response cho ItemFolder operations</summary>
public record ItemFolderResponse(
    Guid ItemId, 
    Guid FolderId, 
    int Position, 
    DateTime AddedAt);
