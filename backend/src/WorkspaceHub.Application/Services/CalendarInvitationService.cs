using System.Text.Json;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

public class CalendarInvitationService : ICalendarInvitationService
{
    private readonly ICalendarInvitationRepository _invitations;
    private readonly IUserRepository _users;
    private readonly IConnectionRepository _connections;
    private readonly ICalendarGateway _calendar;
    private readonly INotificationService _notifications;

    public CalendarInvitationService(
        ICalendarInvitationRepository invitations,
        IUserRepository users,
        IConnectionRepository connections,
        ICalendarGateway calendar,
        INotificationService notifications)
    {
        _invitations = invitations;
        _users = users;
        _connections = connections;
        _calendar = calendar;
        _notifications = notifications;
    }

    public async Task ReconcileOrganizerEventAsync(Item organizerItem, CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        var organizerEmail = (calendarEvent.OrganizerEmail ?? string.Empty).Trim().ToLowerInvariant();
        var attendees = (calendarEvent.FullAttendees ?? Array.Empty<CalendarEventAttendee>())
            .Where(a => !a.Organizer && !string.IsNullOrWhiteSpace(a.Email))
            .GroupBy(a => a.Email.Trim().ToLowerInvariant())
            .Select(g => g.First())
            .ToList();

        // InsertEvent may return only the compact attendee list on some Google accounts.
        if (attendees.Count == 0 && calendarEvent.Attendees != null)
        {
            attendees = calendarEvent.Attendees
                .Where(email => !string.IsNullOrWhiteSpace(email))
                .Select(email => new CalendarEventAttendee(email.Trim(), null, "needsAction", null, false))
                .ToList();
        }

        var existing = await _invitations.GetByOrganizerItemAsync(organizerItem.Id, ct);
        var activeEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notifications = new List<CalendarInvitation>();
        var now = DateTime.UtcNow;

        foreach (var attendee in attendees)
        {
            var email = attendee.Email.Trim().ToLowerInvariant();
            if (email == organizerEmail) continue;
            activeEmails.Add(email);

            var user = await _users.GetByEmailAsync(email, ct);
            if (user == null || !user.IsActive || user.Id == organizerItem.UserId) continue;

            var invitation = existing.FirstOrDefault(x =>
                string.Equals(x.InviteeEmail, email, StringComparison.OrdinalIgnoreCase));
            var googleStatus = ParseStatus(attendee.ResponseStatus);

            if (invitation == null)
            {
                invitation = new CalendarInvitation
                {
                    Id = Guid.NewGuid(),
                    OrganizerItemId = organizerItem.Id,
                    OrganizerUserId = organizerItem.UserId,
                    InviteeUserId = user.Id,
                    InviteeEmail = email,
                    GoogleEventId = calendarEvent.Id,
                    ICalUid = calendarEvent.ICalUid,
                    Status = googleStatus,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                await _invitations.AddAsync(invitation, ct);
                if (googleStatus == CalendarInvitationStatus.NeedsAction)
                    notifications.Add(invitation);
            }
            else
            {
                invitation.GoogleEventId = calendarEvent.Id;
                invitation.ICalUid = calendarEvent.ICalUid ?? invitation.ICalUid;
                // A local RSVP must not be overwritten by Google's stale needsAction during propagation.
                if (ShouldApplyGoogleStatus(invitation, googleStatus))
                    invitation.Status = googleStatus;
                invitation.UpdatedAt = now;
            }
        }

        foreach (var removed in existing.Where(x => !activeEmails.Contains(x.InviteeEmail)))
            _invitations.Remove(removed);

        await _invitations.SaveChangesAsync(ct);

        foreach (var invitation in notifications)
        {
            await _notifications.CreateAndSendAsync(
                invitation.InviteeUserId,
                NotificationType.CalendarInvite,
                "Lời mời tham gia lịch",
                $"Bạn được mời tham gia “{organizerItem.Title}”.",
                $"/calendar?invitation={invitation.Id}",
                ct);
        }
    }

    public async Task ReconcileSyncedEventAsync(Connection connection, Item localItem, CalendarEventDto calendarEvent, CancellationToken ct = default)
    {
        var accountEmail = connection.ProviderAccountId.Trim().ToLowerInvariant();
        var organizerEmail = (calendarEvent.OrganizerEmail ?? string.Empty).Trim().ToLowerInvariant();

        if (organizerEmail == accountEmail)
        {
            var ev = new CalendarEvent(
                calendarEvent.Id, calendarEvent.ETag, calendarEvent.Title, calendarEvent.Snippet,
                calendarEvent.Start, calendarEvent.End, calendarEvent.Location, calendarEvent.Attendees,
                calendarEvent.AllDay, calendarEvent.DriveAttachments, calendarEvent.FullAttendees,
                calendarEvent.MeetUrl, calendarEvent.HtmlLink, calendarEvent.Reminders,
                calendarEvent.Recurrence, calendarEvent.OrganizerEmail, calendarEvent.SelfResponseStatus,
                calendarEvent.ICalUid, calendarEvent.GuestsCanModify, calendarEvent.GuestsCanInviteOthers,
                calendarEvent.GuestsCanSeeOtherGuests);
            await ReconcileOrganizerEventAsync(localItem, ev, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(calendarEvent.ICalUid)) return;
        var invitation = await _invitations.GetByICalUidAndEmailAsync(calendarEvent.ICalUid, accountEmail, ct);
        if (invitation == null || invitation.InviteeUserId != connection.UserId) return;

        invitation.InviteeItemId = localItem.Id;
        if (invitation.GoogleSyncPending && invitation.Status != CalendarInvitationStatus.NeedsAction && localItem.ExternalId != null)
        {
            // User responded locally before connecting GCal: first sync must push that decision up,
            // not overwrite it with Google's still-stale needsAction value.
            try
            {
                await _calendar.RsvpEventAsync(
                    connection, "primary", localItem.ExternalId,
                    ToGoogleStatus(invitation.Status), comment: null, ct: ct);
                invitation.GoogleSyncPending = false;
            }
            catch (Exception ex) when (ex is ProviderException or ForbiddenException)
            {
                // Giữ GoogleSyncPending + Status local; không ghi đè từ Google needsAction.
                invitation.UpdatedAt = DateTime.UtcNow;
                await _invitations.SaveChangesAsync(ct);
                return;
            }
        }
        else
        {
            invitation.Status = ParseStatus(calendarEvent.SelfResponseStatus);
            invitation.GoogleSyncPending = false;
        }
        invitation.UpdatedAt = DateTime.UtcNow;
        if (invitation.Status != CalendarInvitationStatus.NeedsAction)
            invitation.RespondedAt ??= DateTime.UtcNow;
        await _invitations.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CalendarInvitationResponse>> GetForUserAsync(Guid userId, DateTime? from, DateTime? to, CancellationToken ct = default)
        => (await _invitations.GetForInviteeAsync(userId, from, to, ct)).Select(Map).ToList();

    public async Task<CalendarInvitationResponse> GetAsync(Guid userId, Guid invitationId, CancellationToken ct = default)
        => Map(await _invitations.GetByIdForInviteeAsync(invitationId, userId, ct)
            ?? throw new NotFoundException(nameof(CalendarInvitation), invitationId));

    public async Task<CalendarInvitationResponse> RespondAsync(Guid userId, Guid invitationId, RespondCalendarInvitationRequest request, CancellationToken ct = default)
    {
        if (request.Response == CalendarInvitationStatus.NeedsAction)
            throw new BusinessRuleException("Response must be Accepted, Tentative or Declined.");

        var invitation = await _invitations.GetByIdForInviteeAsync(invitationId, userId, ct)
            ?? throw new NotFoundException(nameof(CalendarInvitation), invitationId);

        var gcalConnections = (await _connections.GetByUserIdAsync(userId, ct))
            .Where(x => x.ServiceType == ServiceType.GCal && x.Status == ConnectionStatus.Active)
            .ToList();
        var connection = gcalConnections.FirstOrDefault(x =>
            string.Equals(x.ProviderAccountId, invitation.InviteeEmail, StringComparison.OrdinalIgnoreCase));

        var synced = false;
        if (connection != null)
        {
            CalendarEvent? googleEvent = null;
            if (invitation.InviteeItem?.ExternalId is { Length: > 0 } eventId)
                googleEvent = await _calendar.GetEventAsync(connection, "primary", eventId, ct);
            else if (!string.IsNullOrWhiteSpace(invitation.ICalUid))
                googleEvent = await _calendar.FindEventByICalUidAsync(connection, "primary", invitation.ICalUid, ct);

            if (googleEvent != null)
            {
                await _calendar.RsvpEventAsync(
                    connection, "primary", googleEvent.Id,
                    ToGoogleStatus(request.Response), request.Comment, ct);
                synced = true;
            }
        }

        invitation.Status = request.Response;
        invitation.GoogleSyncPending = !synced;
        invitation.RespondedAt = DateTime.UtcNow;
        invitation.UpdatedAt = DateTime.UtcNow;
        await _invitations.SaveChangesAsync(ct);
        return Map(invitation);
    }

    private static CalendarInvitationStatus ParseStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "accepted" => CalendarInvitationStatus.Accepted,
        "tentative" => CalendarInvitationStatus.Tentative,
        "declined" => CalendarInvitationStatus.Declined,
        _ => CalendarInvitationStatus.NeedsAction
    };

    private static string ToGoogleStatus(CalendarInvitationStatus status) => status switch
    {
        CalendarInvitationStatus.Accepted => "accepted",
        CalendarInvitationStatus.Tentative => "tentative",
        CalendarInvitationStatus.Declined => "declined",
        _ => "needsAction"
    };

    private static bool ShouldApplyGoogleStatus(CalendarInvitation invitation, CalendarInvitationStatus googleStatus)
    {
        if (googleStatus != CalendarInvitationStatus.NeedsAction) return true;
        if (invitation.GoogleSyncPending) return false;
        return invitation.Status == CalendarInvitationStatus.NeedsAction || invitation.RespondedAt == null;
    }

    private static CalendarInvitationResponse Map(CalendarInvitation invitation)
    {
        var meta = ParseMetadata(invitation.OrganizerItem.MetadataJson);
        var allDay = ReadBool(meta, "allDay");
        var start = ReadDate(meta, "start") ?? AsOffset(invitation.OrganizerItem.OccurredAt);
        var end = ReadDate(meta, "end") ?? AsOffset(invitation.OrganizerItem.DueAt ?? invitation.OrganizerItem.OccurredAt.AddHours(1));
        var attendees = ReadStringList(meta, "attendees");
        var canSeeGuests = ReadBool(meta, "guestsCanSeeOtherGuests", true);
        if (!canSeeGuests)
        {
            var organizerEmail = ReadString(meta, "organizerEmail") ?? invitation.OrganizerUser.Email;
            attendees = attendees.Where(x =>
                string.Equals(x, organizerEmail, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x, invitation.InviteeEmail, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return new CalendarInvitationResponse(
            invitation.Id, invitation.OrganizerItemId, invitation.InviteeItemId,
            invitation.InviteeEmail,
            ReadString(meta, "organizerEmail") ?? invitation.OrganizerUser.Email,
            invitation.OrganizerUser.FullName,
            invitation.Status, invitation.GoogleSyncPending,
            invitation.OrganizerItem.Title,
            ReadString(meta, "description") ?? invitation.OrganizerItem.Snippet,
            start, end, allDay, ReadString(meta, "location"), attendees,
            invitation.ICalUid,
            ReadBool(meta, "guestsCanModify"),
            ReadBool(meta, "guestsCanInviteOthers", true),
            canSeeGuests);
    }

    private static Dictionary<string, JsonElement> ParseMetadata(string? json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json ?? "{}") ?? new(); }
        catch (JsonException) { return new(); }
    }

    private static string? ReadString(Dictionary<string, JsonElement> meta, string key)
        => meta.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool ReadBool(Dictionary<string, JsonElement> meta, string key, bool fallback = false)
        => meta.TryGetValue(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : fallback;

    private static List<string> ReadStringList(Dictionary<string, JsonElement> meta, string key)
        => meta.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : new();

    private static DateTimeOffset? ReadDate(Dictionary<string, JsonElement> meta, string key)
        => DateTimeOffset.TryParse(ReadString(meta, key), out var value) ? value : null;

    private static DateTimeOffset AsOffset(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
