namespace WorkspaceHub.Application.DTOs.Emails;

/// <summary>
/// File người dùng tự đính kèm khi Send/Reply/Forward. Nội dung truyền base64 trong JSON body
/// (nhất quán với các endpoint email hiện có; Gmail giới hạn ~25MB/thư nên đủ cho đồ án).
/// </summary>
public class AttachmentUpload
{
    public string Filename { get; set; } = null!;

    /// <summary>MIME type, vd "application/pdf", "image/png". Mặc định octet-stream nếu client không gửi.</summary>
    public string MimeType { get; set; } = "application/octet-stream";

    /// <summary>Nội dung file mã hoá base64 (KHÔNG kèm prefix data URI).</summary>
    public string ContentBase64 { get; set; } = null!;
}
