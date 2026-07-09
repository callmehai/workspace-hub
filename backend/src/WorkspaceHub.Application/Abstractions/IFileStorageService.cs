namespace WorkspaceHub.Application.Abstractions;

/// <summary>
/// Object storage (Cloudflare R2, S3-compatible) — dùng cho avatar (SCRUM-75) và
/// mail attachment ở phase sau. Key tự chọn theo caller (vd "avatars/{userId}.jpg").
/// </summary>
public interface IFileStorageService
{
    /// <summary>Upload và trả về URL public để lưu vào DB (vd Users.AvatarUrl).</summary>
    Task<string> UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Xoá object theo key (vd khi user đổi/xoá avatar). Không throw nếu không tồn tại.</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);
}
