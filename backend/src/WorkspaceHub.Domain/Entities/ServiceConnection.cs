using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Domain.Entities;

/// <summary>1 grant Google bật nhiều sub-service. Gộp luôn cursor incremental sync.</summary>
public class ServiceConnection
{
    public Guid Id { get; set; }
    public Guid OAuthConnectionId { get; set; }
    public ServiceType ServiceType { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? DisplayName { get; set; }
    public CursorType? CursorType { get; set; }
    public string? CursorValue { get; set; }   // null = sync lần đầu
    public DateTime? LastSyncedAt { get; set; }
    public string? LastError { get; set; }

    // Navigation
    public OAuthConnection OAuthConnection { get; set; } = null!;
    public ICollection<Item> Items { get; set; } = new List<Item>();
    public ICollection<ScheduledEmail> ScheduledEmails { get; set; } = new List<ScheduledEmail>();
}
