using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Domain.Entities;

/// <summary>Email hẹn giờ. BE lưu Pending; cron ngoài gọi /process-scheduled mỗi 5 phút.</summary>
public class ScheduledEmail
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ConnectionId { get; set; }           // phải là Connection có ServiceType=Gmail
    public string ToJson { get; set; } = "[]";
    public string CcJson { get; set; } = "[]";
    public string BccJson { get; set; } = "[]";
    public string Subject { get; set; } = null!;
    public string BodyHtml { get; set; } = null!;
    /// <summary>File đính kèm (base64) — JSON mảng AttachmentUpload. Cron gửi kèm khi tới hạn.</summary>
    public string AttachmentsJson { get; set; } = "[]";
    public DateTime SendAt { get; set; }
    public ScheduledEmailStatus Status { get; set; } = ScheduledEmailStatus.Pending;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
    public Connection Connection { get; set; } = null!;
}
