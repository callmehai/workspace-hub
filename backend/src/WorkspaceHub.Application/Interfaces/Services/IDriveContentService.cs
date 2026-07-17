using WorkspaceHub.Application.Abstractions;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// Đọc nội dung file Drive (tải xuống + thumbnail preview) — proxy media qua BE bằng token
/// của connection. Tách khỏi <see cref="IDriveSharingService"/> (tạo folder + chia sẻ) vì đây là
/// luồng READ; controller stream trực tiếp <see cref="DriveMediaResult"/> ra Response.Body.
/// </summary>
public interface IDriveContentService
{
    /// <summary>
    /// Tải nội dung file về (proxy). Google-native docs được export sang PDF/PNG.
    /// Ném <see cref="Common.BusinessRuleException"/> nếu item là folder / không phải file Drive.
    /// </summary>
    Task<DriveMediaResult> DownloadAsync(Guid userId, Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// Thumbnail của file để preview (ảnh/PDF/video…). <c>null</c> nếu file không có thumbnail
    /// (folder, hoặc Google chưa render) — caller trả 204.
    /// </summary>
    Task<DriveMediaResult?> GetThumbnailAsync(Guid userId, Guid itemId, CancellationToken ct = default);
}
