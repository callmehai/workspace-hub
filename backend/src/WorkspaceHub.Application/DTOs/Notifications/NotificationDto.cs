using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.DTOs.Notifications;

/// <summary>DTO notification — class (giống ScheduledEmailDto) để OData in-memory filter/order ổn định.</summary>
public class NotificationDto
{
    public Guid Id { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = null!;
    public string Body { get; set; } = null!;
    public string LinkUrl { get; set; } = null!;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
