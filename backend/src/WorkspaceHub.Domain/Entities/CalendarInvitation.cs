using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Domain.Entities;

/// <summary>
/// Lời mời Calendar nội bộ. Google Calendar vẫn là nguồn sự thật của event;
/// bản ghi này giúp WorkspaceHub thông báo và nhận RSVP ngay cả khi khách chưa kết nối GCal.
/// </summary>
public class CalendarInvitation
{
    public Guid Id { get; set; }
    public Guid OrganizerItemId { get; set; }
    public Guid OrganizerUserId { get; set; }
    public Guid InviteeUserId { get; set; }
    public Guid? InviteeItemId { get; set; }
    public string InviteeEmail { get; set; } = null!;
    public string GoogleEventId { get; set; } = null!;
    public string? ICalUid { get; set; }
    public CalendarInvitationStatus Status { get; set; } = CalendarInvitationStatus.NeedsAction;
    public bool GoogleSyncPending { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }

    public Item OrganizerItem { get; set; } = null!;
    public User OrganizerUser { get; set; } = null!;
    public User InviteeUser { get; set; } = null!;
    public Item? InviteeItem { get; set; }
}
