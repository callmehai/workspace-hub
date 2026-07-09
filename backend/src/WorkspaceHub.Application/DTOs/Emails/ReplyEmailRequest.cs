namespace WorkspaceHub.Application.DTOs.Emails;

/// <summary>Request body cho POST /api/emails/reply — reply hoặc reply all.</summary>
public class ReplyEmailRequest
{
    public Guid ConnectionId { get; set; }

    /// <summary>Item email đang reply — dùng để lấy threadId, messageId gốc, subject, recipients.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Nội dung HTML của reply.</summary>
    public string BodyHtml { get; set; } = null!;

    /// <summary>true = Reply All (giữ cc + to gốc), false = Reply (chỉ gửi cho sender gốc).</summary>
    public bool ReplyAll { get; set; }

    /// <summary>CC bổ sung thêm (ngoài cc gốc nếu reply all).</summary>
    public List<string> Cc { get; set; } = new();

    /// <summary>BCC bổ sung.</summary>
    public List<string> Bcc { get; set; } = new();

    /// <summary>File người dùng tự đính kèm vào reply (tùy chọn).</summary>
    public List<AttachmentUpload> Attachments { get; set; } = new();
}
