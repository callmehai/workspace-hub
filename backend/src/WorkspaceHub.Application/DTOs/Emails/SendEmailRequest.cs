namespace WorkspaceHub.Application.DTOs.Emails;

/// <summary>Gửi email trực tiếp (gửi ngay) qua Gmail — như CreateScheduledEmailRequest nhưng KHÔNG có SendAt.</summary>
public class SendEmailRequest
{
    public Guid ConnectionId { get; set; }
    public List<string> To { get; set; } = new();
    public List<string> Cc { get; set; } = new();
    public List<string> Bcc { get; set; } = new();
    public string Subject { get; set; } = null!;
    public string BodyHtml { get; set; } = null!;

    /// <summary>File người dùng tự đính kèm (tùy chọn).</summary>
    public List<AttachmentUpload> Attachments { get; set; } = new();
}
