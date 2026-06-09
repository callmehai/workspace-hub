using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Domain.Entities;

/// <summary>Email hẹn giờ. BE lưu Pending; cron ngoài gọi /process-scheduled mỗi 5 phút.</summary>
public class ScheduledEmail
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ServiceConnectionId { get; set; }    // chỉ Gmail gửi được
    public string ToJson { get; set; } = "[]";
    public string CcJson { get; set; } = "[]";
    public string BccJson { get; set; } = "[]";
    public string Subject { get; set; } = null!;
    public string BodyHtml { get; set; } = null!;
    public DateTime SendAt { get; set; }
    public ScheduledEmailStatus Status { get; set; } = ScheduledEmailStatus.Pending;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? SentAt { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public ServiceConnection ServiceConnection { get; set; } = null!;
}
