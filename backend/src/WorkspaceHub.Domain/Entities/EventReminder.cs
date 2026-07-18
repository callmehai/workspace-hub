using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Domain.Entities;

/// <summary>Calendar event reminder configuration — one row = one ReminderType + one offset.</summary>
public class EventReminder
{
    public Guid Id { get; set; }
    public Guid EventItemId { get; set; } // FK -> Items.Id
    public ReminderType ReminderType { get; set; } = ReminderType.GooglePopup;
    public int OffsetValue { get; set; }
    public ReminderUnit OffsetUnit { get; set; }
    public string? TimeOfDay { get; set; } // "HH:mm", for Days/Weeks reminders.
    public bool IsSent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Item EventItem { get; set; } = null!;
}
