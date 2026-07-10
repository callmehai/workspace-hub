namespace WorkspaceHub.Application.DTOs.Emails;

/// <summary>Request body cho POST /api/emails/forward — forward email cho người khác.</summary>
public class ForwardEmailRequest
{
    public Guid ConnectionId { get; set; }

    /// <summary>Item email đang forward — dùng để lấy threadId, body gốc, attachments.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Danh sách người nhận forward.</summary>
    public List<string> To { get; set; } = new();

    /// <summary>CC.</summary>
    public List<string> Cc { get; set; } = new();

    /// <summary>BCC.</summary>
    public List<string> Bcc { get; set; } = new();

    /// <summary>Nội dung HTML user thêm trước phần forward (vd "FYI, see below").</summary>
    public string BodyHtml { get; set; } = null!;

    /// <summary>Có đính kèm attachment từ email gốc không. Mặc định true.</summary>
    public bool IncludeAttachments { get; set; } = true;

    /// <summary>File người dùng tự đính kèm thêm khi forward (ngoài file gốc, tùy chọn).</summary>
    public List<AttachmentUpload> Attachments { get; set; } = new();
}
