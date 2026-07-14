using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.DTOs;

public record CalendarInvitationResponse(
    Guid Id,
    Guid OrganizerItemId,
    Guid? InviteeItemId,
    string InviteeEmail,
    string OrganizerEmail,
    string OrganizerName,
    CalendarInvitationStatus Status,
    bool GoogleSyncPending,
    string Title,
    string? Description,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool AllDay,
    string? Location,
    List<string> Attendees,
    string? ICalUid,
    bool GuestsCanModify,
    bool GuestsCanInviteOthers,
    bool GuestsCanSeeOtherGuests);

public record RespondCalendarInvitationRequest(
    CalendarInvitationStatus Response,
    string? Comment = null);
