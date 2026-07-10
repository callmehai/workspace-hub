namespace WorkspaceHub.Application.Abstractions;

/// <summary>Google Drive file đính kèm trong Calendar Event.</summary>
public record CalendarDriveAttachment(string FileId, string? Title, string? MimeType, string? FileUrl);

public record CalendarEvent(
    string Id,
    string? ETag,
    string? Summary,
    string? Description,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    string? Location = null,
    IReadOnlyList<string>? Attendees = null,
    bool AllDay = false,
    IReadOnlyList<CalendarDriveAttachment>? DriveAttachments = null);
