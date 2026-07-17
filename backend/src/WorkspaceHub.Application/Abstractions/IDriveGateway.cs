using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Abstractions;

public interface IDriveGateway
{
    //Giao tiếp với Google
    Task<DriveFile> GetFileAsync(Connection connection, string fileId, CancellationToken ct = default);
    Task<DriveFile> UpdateFileAsync(Connection connection, string fileId, string newName, CancellationToken ct = default);
    Task<DriveFile> TrashFileAsync(Connection connection, string fileId, CancellationToken ct = default);
    Task<DriveFile> UntrashFileAsync(Connection connection, string fileId, CancellationToken ct = default);

    //Tạo folder trên Google Drive. parentExternalId null = My Drive root.
    Task<DriveFileDto> CreateFolderAsync(
        Connection connection,
        string name,
        string?parentExternalId,
        CancellationToken ct= default);

    /// <summary>
    /// Upload file binary từ máy người dùng lên Google Drive.
    /// Gọi Google API <c>files.create</c> kèm nội dung stream — BE proxy, không load hết file vào RAM.
    /// </summary>
    /// <param name="connection">Connection Drive đã OAuth (token lấy qua <see cref="ITokenService"/>).</param>
    /// <param name="name">Tên file hiển thị trên Drive (vd. report.pdf).</param>
    /// <param name="mimeType">MIME type (vd. application/pdf). Dùng application/octet-stream nếu không biết.</param>
    /// <param name="parentExternalId">Google file id của folder cha. Null = upload vào gốc My Drive.</param>
    /// <param name="content">Stream nội dung file — đọc tuần tự khi Google client upload.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Metadata file vừa tạo trên Drive (id, name, mimeType, size, parents…).</returns>
    Task<DriveFileDto> UploadFileAsync(
        Connection connection,
        string name,
        string mimeType,
        string? parentExternalId,
        Stream content,
        CancellationToken ct = default);

    // Danh sách quyền share của file/folder trên Google Drive.
    Task<IReadOnlyList<DrivePermissionDto>> ListPermissionsAsync(
        Connection connection,
        string fileId,
        CancellationToken ct = default);

    // Tạo quyền share cho user (email) trên file/folder Google Drive.
    Task<DrivePermissionDto> CreateUserPermissionAsync(
        Connection connection,
        string fileId,
        string email,
        DrivePermissionRole role,
        bool notify,
        CancellationToken ct = default);

    //Update quyền share (role) cho user trên file/folder Google Drive.
    Task<DrivePermissionDto> UpdatePermissionAsync(
        Connection connection,
        string fileId,
        string permissionId,
        DrivePermissionRole role,
        CancellationToken ct = default);

    //Xóa quyền share (permissionId) trên file/folder Google Drive.
    Task DeletePermissionAsync(
    Connection connection,
    string fileId,
    string permissionId,
    CancellationToken ct = default);

    // Bật/tắt link share (anyone with link) trên file/folder Google Drive.
    Task<DrivePermissionDto?> SetLinkSharingAsync(
        Connection connection,
        string fileId,
        bool enable,
        DrivePermissionRole role,
        CancellationToken ct = default);

    /// <summary>
    /// Tải nội dung file (proxy media) — stream về không buffer toàn bộ vào RAM.
    /// File Google-native (Docs/Sheets/Slides/Drawing) được EXPORT sang PDF/PNG (không tải trực tiếp được).
    /// </summary>
    /// <param name="mimeType">MIME của file (quyết định download alt=media vs export).</param>
    /// <param name="downloadName">Tên gợi ý cho Content-Disposition (kèm đuôi export nếu cần).</param>
    /// <returns><see cref="DriveMediaResult"/> — caller phải DisposeAsync sau khi stream xong.</returns>
    Task<DriveMediaResult> DownloadFileAsync(
        Connection connection,
        string fileId,
        string mimeType,
        string? downloadName,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy thumbnail của file (nếu Google có). Thumbnail là "nice to have":
    /// file không có thumbnail hoặc fetch lỗi → trả <c>null</c> (không ném).
    /// </summary>
    Task<DriveMediaResult?> GetThumbnailAsync(
        Connection connection,
        string fileId,
        CancellationToken ct = default);
}
