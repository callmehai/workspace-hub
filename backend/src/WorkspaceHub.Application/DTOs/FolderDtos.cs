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

/// <summary>POST /api/folders/{folderId}/items/bulk — gắn nhiều item vào folder</summary>
public record AddItemsToFolderBulkRequest(List<Guid> ItemIds);

/// <summary>DELETE /api/folders/{folderId}/items/bulk — xóa nhiều item khỏi folder</summary>
public record RemoveItemsFromFolderBulkRequest(List<Guid> ItemIds);

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

// ───────────────────────── Folder Sharing DTOs ─────────────────────────

/// <summary>POST /api/folders/{id}/shares — mời bạn bè vào folder.</summary>
public record InviteFolderShareRequest(
    Guid FriendUserId,
    string Permission);  // "Viewer" | "Editor"

/// <summary>PATCH /api/folders/{id}/shares/{shareId} — đổi quyền của 1 share.</summary>
public record UpdateFolderShareRequest(
    string Permission);  // "Viewer" | "Editor"

/// <summary>
/// Response cho 1 share entry (owner xem danh sách ai được share).
/// Status: "Pending" (AcceptedAt=null) | "Accepted" (AcceptedAt!=null).
/// </summary>
public record FolderShareDto(
    Guid ShareId,
    Guid FolderId,
    string FolderName,
    Guid SharedWithUserId,
    string SharedWithUserName,
    string? SharedWithUserAvatar,
    string Permission,
    string Status,
    DateTime SharedAt);

/// <summary>
/// Folder được chia sẻ với user hiện tại (GET /api/folders/shared-with-me).
/// </summary>
public record SharedFolderDto(
    Guid ShareId,
    Guid FolderId,
    string FolderName,
    Guid OwnerUserId,
    string OwnerName,
    string Permission,
    string Status,
    DateTime SharedAt);

