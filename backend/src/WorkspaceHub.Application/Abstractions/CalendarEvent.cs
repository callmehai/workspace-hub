namespace WorkspaceHub.Application.Abstractions;

public record CalendarEvent(
    string Id,
    string? ETag,
    string? Summary,
    string? Description,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    string? Location = null,
    IReadOnlyList<string>? Attendees = null);
