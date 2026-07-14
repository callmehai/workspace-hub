using System.Collections.Generic;
using System;

namespace WorkspaceHub.Application.Abstractions;

/// <summary>Google Drive file đính kèm trong Calendar Event.</summary>
public record CalendarDriveAttachment(string FileId, string? Title, string? MimeType, string? FileUrl);

public record CalendarEventAttendee(
    string Email,
    string? DisplayName,
    string? ResponseStatus, // "accepted", "declined", "tentative", "needsAction"
    string? Comment,
    bool Organizer);

public record CalendarEventReminder(
    string Method, // "popup" hoặc "email"
    int Minutes);

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
    IReadOnlyList<CalendarDriveAttachment>? DriveAttachments = null,
    IReadOnlyList<CalendarEventAttendee>? FullAttendees = null,
    string? MeetUrl = null,
    string? HtmlLink = null,
    IReadOnlyList<CalendarEventReminder>? Reminders = null,
    IReadOnlyList<string>? Recurrence = null,
    string? OrganizerEmail = null,
    string? SelfResponseStatus = null,
    string? ICalUid = null,
    bool? GuestsCanModify = null,
    bool? GuestsCanInviteOthers = null,
    bool? GuestsCanSeeOtherGuests = null,
    bool SendUpdates = true);
