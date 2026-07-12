using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Domain.Entities;

/// <summary>Cấu hình nhắc nhở sự kiện lịch (Google Calendar-parity).</summary>
public class EventReminder
{
    public Guid Id { get; set; }
    public Guid EventItemId { get; set; } // FK -> Items.Id
    public ReminderType ReminderType { get; set; }
    public int OffsetValue { get; set; }
    public ReminderUnit OffsetUnit { get; set; }
    public string? TimeOfDay { get; set; } // "HH:mm" (ví dụ "23:30") cho Days/Weeks
    public bool IsSent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Item EventItem { get; set; } = null!;
}
