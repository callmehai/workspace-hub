namespace WorkspaceHub.Application.Abstractions;

public class CalendarEventDto
{
    public string Id { get; set; } = string.Empty;
    public string? ETag { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public DateTimeOffset? Start { get; set; }
    public DateTimeOffset? End { get; set; }
    public bool AllDay { get; set; }
    public string? Location { get; set; }
    public List<string> Attendees { get; set; } = new();
    public string? MeetUrl { get; set; }
    /// <summary>Link mở event trong Google Calendar (event.htmlLink) — cho nút "Mở trong Calendar".</summary>
    public string? HtmlLink { get; set; }
    public List<CalendarDriveAttachment> DriveAttachments { get; set; } = new();
    public List<CalendarEventReminder> Reminders { get; set; } = new();
    public List<string> Recurrence { get; set; } = new();
}

public class CalendarSyncResult
{
    public bool Expired { get; set; }
    public List<CalendarEventDto> Events { get; set; } = new();
    public List<string> CancelledEventIds { get; set; } = new();
    public string? NextSyncToken { get; set; }

    public CalendarSyncResult(bool expired, List<CalendarEventDto> events, string? nextSyncToken, List<string>? cancelledEventIds = null)
    {
        Expired = expired;
        Events = events;
        NextSyncToken = nextSyncToken;
        CancelledEventIds = cancelledEventIds ?? new List<string>();
    }
}
