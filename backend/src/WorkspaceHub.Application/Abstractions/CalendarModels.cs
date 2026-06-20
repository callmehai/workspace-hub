namespace WorkspaceHub.Application.Abstractions;

public class CalendarEventDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public DateTimeOffset? Start { get; set; }
    public DateTimeOffset? End { get; set; }
    public string? Location { get; set; }
    public List<string> Attendees { get; set; } = new();
    public string? MeetUrl { get; set; }
    public DateTimeOffset? OccurredAt { get; set; }
}

public class CalendarSyncResult
{
    public bool Expired { get; set; }
    public List<CalendarEventDto> Events { get; set; } = new();
    public string? NextSyncToken { get; set; }

    public CalendarSyncResult(bool expired, List<CalendarEventDto> events, string? nextSyncToken)
    {
        Expired = expired;
        Events = events;
        NextSyncToken = nextSyncToken;
    }
}
