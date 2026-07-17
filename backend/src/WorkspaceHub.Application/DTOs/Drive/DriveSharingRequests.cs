namespace WorkspaceHub.Application.DTOs.Drive;

/// <summary>
/// Request body cho các endpoint /api/drive/* (SCRUM-79 — A4).
/// Controller nhận JSON → map sang tham số <see cref="Interfaces.Services.IDriveSharingService"/>.
/// </summary>

/// <summary>POST /api/drive/folders — tạo folder trên Google Drive.</summary>
/// <param name="ConnectionId">Connection Drive (Guid trong DB).</param>
/// <param name="Name">Tên folder hiển thị trên Drive.</param>
/// <param name="ParentItemId">Item folder cha trong app (null = gốc My Drive).</param>
public record CreateDriveFolderRequest(
    Guid ConnectionId,
    string Name,
    Guid? ParentItemId = null);

/// <summary>POST /api/drive/items/{itemId}/permissions — mời email chia sẻ.</summary>
/// <param name="Email">Email người được mời.</param>
/// <param name="Role">reader | commenter | writer (chuỗi lowercase, khớp Google API).</param>
/// <param name="Notify">true = Google gửi email thông báo.</param>
public record AddDrivePermissionRequest(
    string Email,
    string Role,
    bool Notify = true);

/// <summary>PATCH /api/drive/items/{itemId}/permissions/{permissionId} — đổi quyền.</summary>
/// <param name="Role">reader | commenter | writer.</param>
public record UpdateDrivePermissionRequest(
    string Role);

/// <summary>PUT /api/drive/items/{itemId}/link-sharing — bật/tắt "ai có link".</summary>
/// <param name="Enabled">true = bật link công khai; false = tắt (xóa permission anyone).</param>
/// <param name="Role">Bắt buộc khi Enabled=true — quyền cho người mở link. Bỏ qua khi tắt.</param>
/// <param name="ConfirmRestrictParent">
/// Case 1 (giống Drive): true = user đã confirm popup "Xoá khỏi thư mục mẹ"
/// → tắt link cả file lẫn folder mẹ. Chỉ có ý nghĩa khi Enabled=false.
/// </param>
public record LinkSharingRequest(
    bool Enabled,
    string? Role = null,
    bool ConfirmRestrictParent = false);
