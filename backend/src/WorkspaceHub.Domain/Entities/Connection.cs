using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Domain.Entities;

/// <summary>
/// Mô hình B: mỗi service = 1 connection độc lập, token riêng (thay OAuthConnections + ServiceConnections).
/// Không lưu Scopes (suy từ ServiceType trong code) và Permission (bật là full quyền).
/// Disconnect = xoá đúng row; Items giữ lại (ConnectionId set null ở service layer).
/// </summary>
public class Connection
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid IntegrationId { get; set; }
    public ProviderType Provider { get; set; }
    public ServiceType ServiceType { get; set; }
    public string ProviderAccountId { get; set; } = null!;  // email/sub/cloudId — phân biệt nhiều account
    public string AccessTokenEncrypted { get; set; } = null!;
    public string RefreshTokenEncrypted { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }                  // refresh nếu còn < 5 phút
    public ConnectionStatus Status { get; set; } = ConnectionStatus.Active;
    public CursorType? CursorType { get; set; }
    public string? CursorValue { get; set; }                 // null = sync lần đầu
    public DateTime? LastSyncedAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public Integration Integration { get; set; } = null!;
    public ICollection<Item> Items { get; set; } = new List<Item>();
    public ICollection<ScheduledEmail> ScheduledEmails { get; set; } = new List<ScheduledEmail>();
}
