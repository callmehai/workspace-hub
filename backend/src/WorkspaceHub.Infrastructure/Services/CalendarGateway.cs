using System.Globalization;
using System.Text.RegularExpressions;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Infrastructure.Services;

public class CalendarGateway : ICalendarGateway
{
    private readonly ITokenService _tokenService;

    public CalendarGateway(ITokenService tokenService)
    {
        _tokenService = tokenService;
    }

    private async Task<CalendarService> BuildCalendarServiceAsync(Connection connection, CancellationToken ct)
    {
        var accessToken = await _tokenService.GetFreshAccessTokenAsync(connection, ct);
        var credential = GoogleCredential.FromAccessToken(accessToken);
        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "WorkspaceHub"
        });
    }

    public async Task<CalendarSyncResult> SyncEventsAsync(Connection connection, string? syncToken, CancellationToken ct = default)
    {
        using var service = await BuildCalendarServiceAsync(connection, ct);
        var request = service.Events.List("primary");

        if (!string.IsNullOrEmpty(syncToken))
            request.SyncToken = syncToken;
        else
            request.TimeMinDateTimeOffset = DateTimeOffset.UtcNow.AddMonths(-3);

        // Expand recurring events into instances (required for initial sync; safe with syncToken).
        request.SingleEvents = true;

        var eventsDto = new List<CalendarEventDto>();
        var cancelledIds = new List<string>();
        string? nextSyncToken = null;

        try
        {
            do
            {
                var response = await request.ExecuteAsync(ct);

                if (response.Items != null)
                {
                    foreach (var item in response.Items)
                    {
                        if (string.Equals(item.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(item.Id))
                                cancelledIds.Add(item.Id);
                            continue;
                        }

                        var start = ParseEventDateTime(item.Start);
                        var end = ParseEventDateTime(item.End);
                        var allDay = item.Start?.Date != null;

                        var selfAttendee = item.Attendees?.FirstOrDefault(a => a.Self == true);
                        eventsDto.Add(new CalendarEventDto
                        {
                            Id = item.Id ?? string.Empty,
                            ETag = item.ETag,
                            Title = item.Summary ?? string.Empty,
                            Snippet = item.Description ?? string.Empty,
                            Start = start,
                            End = end,
                            AllDay = allDay,
                            Location = item.Location,
                            Attendees = item.Attendees?
                                .Select(a => a.Email)
                                .Where(e => !string.IsNullOrEmpty(e))
                                .ToList() ?? [],
                            MeetUrl = item.HangoutLink,
                            HtmlLink = item.HtmlLink,
                            DriveAttachments = MapAttachments(item.Attachments),
                            Reminders = item.Reminders?.UseDefault == false && item.Reminders.Overrides != null
                                ? item.Reminders.Overrides.Select(r => new CalendarEventReminder(r.Method ?? "popup", r.Minutes ?? 0)).ToList()
                                : new List<CalendarEventReminder>(),
                            Recurrence = item.Recurrence != null ? item.Recurrence.ToList() : new List<string>(),
                            OrganizerEmail = item.Organizer?.Email,
                            SelfResponseStatus = selfAttendee?.ResponseStatus ?? (item.Organizer?.Self == true ? "accepted" : "needsAction"),
                            ICalUid = item.ICalUID,
                            FullAttendees = item.Attendees?.Select(a => new CalendarEventAttendee(
                                a.Email ?? string.Empty, a.DisplayName, a.ResponseStatus, a.Comment, a.Organizer ?? false)).ToList() ?? [],
                            GuestsCanModify = item.GuestsCanModify ?? false,
                            GuestsCanInviteOthers = item.GuestsCanInviteOthers ?? true,
                            GuestsCanSeeOtherGuests = item.GuestsCanSeeOtherGuests ?? true,
                        });
                    }
                }

                request.PageToken = response.NextPageToken;
                nextSyncToken = response.NextSyncToken;

            } while (!string.IsNullOrEmpty(request.PageToken));

            return new CalendarSyncResult(false, eventsDto, nextSyncToken, cancelledIds);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Gone || ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new CalendarSyncResult(true, new List<CalendarEventDto>(), null);
        }
    }

    public async Task<CalendarEvent> GetEventAsync(Connection connection, string calendarId, string eventId, CancellationToken ct = default)
    {
        try
        {
            using var calendar = await BuildCalendarServiceAsync(connection, ct);
            var ev = await calendar.Events.Get(calendarId, eventId).ExecuteAsync(ct);
            return MapToDto(ev);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Calendar", "Event", eventId, forbiddenMessage: "Cần reconnect.");
        }
    }

    public async Task<CalendarEvent> UpdateEventAsync(Connection connection, string calendarId, string eventId, CalendarEvent eventDto, CancellationToken ct = default)
    {
        try
        {
            using var calendar = await BuildCalendarServiceAsync(connection, ct);

            var existing = await calendar.Events.Get(calendarId, eventId).ExecuteAsync(ct);
            var hasGuestUpdates = eventDto.Attendees != null || existing.Attendees?.Count > 0;

            if (eventDto.Summary != null) existing.Summary = eventDto.Summary;
            if (eventDto.Description != null) existing.Description = eventDto.Description;
            if (eventDto.Location != null) existing.Location = eventDto.Location;
            if (eventDto.Attendees != null)
                existing.Attendees = eventDto.Attendees.Select(a => new EventAttendee { Email = a }).ToList();
            if (eventDto.GuestsCanModify.HasValue) existing.GuestsCanModify = eventDto.GuestsCanModify.Value;
            if (eventDto.GuestsCanInviteOthers.HasValue) existing.GuestsCanInviteOthers = eventDto.GuestsCanInviteOthers.Value;
            if (eventDto.GuestsCanSeeOtherGuests.HasValue) existing.GuestsCanSeeOtherGuests = eventDto.GuestsCanSeeOtherGuests.Value;

            ApplyUpdateTimes(existing, eventDto);

            if (eventDto.DriveAttachments != null)
            {
                existing.Attachments = eventDto.DriveAttachments.Select(a => new EventAttachment
                {
                    FileId = a.FileId,
                    Title = a.Title,
                    MimeType = a.MimeType,
                    FileUrl = a.FileUrl
                }).ToList();
            }

            // Reminders: null = giữ nguyên từ Events.Get; non-null (kể cả rỗng) = ghi overrides
            if (eventDto.Reminders != null)
            {
                existing.Reminders = new Event.RemindersData
                {
                    UseDefault = false,
                    Overrides = eventDto.Reminders.Select(r => new Google.Apis.Calendar.v3.Data.EventReminder
                    {
                        Method = r.Method,
                        Minutes = r.Minutes
                    }).ToList()
                };
            }

            // Recurrence
            if (eventDto.Recurrence != null)
            {
                existing.Recurrence = eventDto.Recurrence.Count > 0 ? eventDto.Recurrence.ToList() : null;
            }

            var request = calendar.Events.Update(existing, calendarId, eventId);
            if (hasGuestUpdates)
            {
                request.SendUpdates = eventDto.SendUpdates
                    ? EventsResource.UpdateRequest.SendUpdatesEnum.All
                    : EventsResource.UpdateRequest.SendUpdatesEnum.None;
            }
            if (eventDto.DriveAttachments != null)
                request.SupportsAttachments = true;

            var updated = await request.ExecuteAsync(ct);
            return MapToDto(updated);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Calendar", "Event", eventId);
        }
    }

    public async Task<CalendarEvent> InsertEventAsync(Connection connection, string calendarId, CalendarEvent eventDto, CancellationToken ct = default)
    {
        try
        {
            using var calendar = await BuildCalendarServiceAsync(connection, ct);

            Event ev;
            if (eventDto.AllDay)
            {
                // All-day event: Google Calendar yêu cầu date-only format ("YYYY-MM-DD"), không có time.
                var startDate = eventDto.Start?.ToString("yyyy-MM-dd") ?? DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
                var endDate = eventDto.End?.ToString("yyyy-MM-dd") ?? eventDto.Start?.AddDays(1).ToString("yyyy-MM-dd") ?? DateTimeOffset.UtcNow.AddDays(1).ToString("yyyy-MM-dd");
                ev = new Event
                {
                    Summary = eventDto.Summary,
                    Description = eventDto.Description,
                    Location = eventDto.Location,
                    Attendees = eventDto.Attendees?.Select(a => new EventAttendee { Email = a }).ToList(),
                    Start = new EventDateTime { Date = startDate },
                    End = new EventDateTime { Date = endDate }
                };
            }
            else
            {
                ev = new Event
                {
                    Summary = eventDto.Summary,
                    Description = eventDto.Description,
                    Location = eventDto.Location,
                    Attendees = eventDto.Attendees?.Select(a => new EventAttendee { Email = a }).ToList(),
                    Start = eventDto.Start.HasValue ? ToTimedEventDateTime(eventDto.Start.Value) : null,
                    End = eventDto.End.HasValue ? ToTimedEventDateTime(eventDto.End.Value) : null
                };
            }

            ev.GuestsCanModify = eventDto.GuestsCanModify ?? false;
            ev.GuestsCanInviteOthers = eventDto.GuestsCanInviteOthers ?? true;
            ev.GuestsCanSeeOtherGuests = eventDto.GuestsCanSeeOtherGuests ?? true;

            // Drive file attachments
            if (eventDto.DriveAttachments != null && eventDto.DriveAttachments.Count > 0)
            {
                ev.Attachments = eventDto.DriveAttachments.Select(a => new EventAttachment
                {
                    FileId = a.FileId,
                    Title = a.Title,
                    MimeType = a.MimeType,
                    FileUrl = a.FileUrl
                }).ToList();
            }

            // Reminders
            if (eventDto.Reminders != null)
            {
                ev.Reminders = new Event.RemindersData
                {
                    UseDefault = false,
                    Overrides = eventDto.Reminders.Select(r => new Google.Apis.Calendar.v3.Data.EventReminder
                    {
                        Method = r.Method,
                        Minutes = r.Minutes
                    }).ToList()
                };
            }
            else
            {
                ev.Reminders = new Event.RemindersData { UseDefault = true };
            }

            // Recurrence
            if (eventDto.Recurrence != null && eventDto.Recurrence.Count > 0)
            {
                ev.Recurrence = eventDto.Recurrence.ToList();
            }

            var insertRequest = calendar.Events.Insert(ev, calendarId);
            if (ev.Attendees?.Count > 0)
            {
                insertRequest.SendUpdates = eventDto.SendUpdates
                    ? EventsResource.InsertRequest.SendUpdatesEnum.All
                    : EventsResource.InsertRequest.SendUpdatesEnum.None;
            }
            if (ev.Attachments?.Count > 0)
                insertRequest.SupportsAttachments = true;

            var created = await insertRequest.ExecuteAsync(ct);
            return MapToDto(created);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Calendar", "Calendar", calendarId);
        }
    }

    public async Task DeleteEventAsync(Connection connection, string calendarId, string eventId, CancellationToken ct = default)
    {
        try
        {
            using var calendar = await BuildCalendarServiceAsync(connection, ct);
            var request = calendar.Events.Delete(calendarId, eventId);
            request.SendUpdates = EventsResource.DeleteRequest.SendUpdatesEnum.All;
            await request.ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Calendar", "Event", eventId);
        }
    }

    public async Task RsvpEventAsync(Connection connection, string calendarId, string eventId, string responseStatus, string? comment, CancellationToken ct = default)
    {
        try
        {
            using var service = await BuildCalendarServiceAsync(connection, ct);
            var ev = await service.Events.Get(calendarId, eventId).ExecuteAsync(ct);

            if (ev.Attendees == null)
            {
                ev.Attendees = new List<EventAttendee>();
            }

            var attendee = ev.Attendees.FirstOrDefault(a => string.Equals(a.Email, connection.ProviderAccountId, StringComparison.OrdinalIgnoreCase));
            if (attendee == null)
            {
                attendee = new EventAttendee { Email = connection.ProviderAccountId };
                ev.Attendees.Add(attendee);
            }

            attendee.ResponseStatus = responseStatus;
            if (comment != null)
            {
                attendee.Comment = comment;
            }

            var request = service.Events.Update(ev, calendarId, eventId);
            request.SendUpdates = EventsResource.UpdateRequest.SendUpdatesEnum.All;
            await request.ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Calendar", "Event", eventId);
        }
    }

    public async Task<CalendarEvent?> FindEventByICalUidAsync(Connection connection, string calendarId, string iCalUid, CancellationToken ct = default)
    {
        try
        {
            using var service = await BuildCalendarServiceAsync(connection, ct);
            var request = service.Events.List(calendarId);
            request.ICalUID = iCalUid;
            request.SingleEvents = true;
            request.ShowDeleted = false;
            request.MaxResults = 10;
            var response = await request.ExecuteAsync(ct);
            var ev = response.Items?.FirstOrDefault(x => !string.Equals(x.Status, "cancelled", StringComparison.OrdinalIgnoreCase));
            return ev == null ? null : MapToDto(ev);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Calendar", "Event", iCalUid);
        }
    }

    // ───────────────────────── Private helpers ─────────────────────────

    private static EventDateTime ToAllDayEventDateTime(DateTimeOffset value)
        => new() { Date = value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };

    private static EventDateTime ToTimedEventDateTime(DateTimeOffset value)
        => new() { DateTimeDateTimeOffset = value, TimeZone = "UTC" };

    /// <summary>Gán start/end lên Google Event đã GET — thay whole Start/End (chỉ date HOẶC dateTime).</summary>
    private static void ApplyUpdateTimes(Event existing, CalendarEvent dto)
    {
        if (!dto.Start.HasValue && !dto.End.HasValue)
            return;

        // Caller must send start+end together when changing times (avoids mixed date/dateTime on Google).
        if (dto.Start.HasValue != dto.End.HasValue)
            return;

        if (dto.AllDay)
        {
            if (dto.Start.HasValue)
                existing.Start = ToAllDayEventDateTime(dto.Start.Value);
            if (dto.End.HasValue)
                existing.End = ToAllDayEventDateTime(dto.End.Value);
        }
        else
        {
            if (dto.Start.HasValue)
                existing.Start = ToTimedEventDateTime(dto.Start.Value);
            if (dto.End.HasValue)
                existing.End = ToTimedEventDateTime(dto.End.Value);
        }
    }

    private static DateTimeOffset? ParseEventDateTime(EventDateTime? eventTime)
    {
        if (eventTime?.DateTimeDateTimeOffset != null)
            return eventTime.DateTimeDateTimeOffset;

        if (!string.IsNullOrEmpty(eventTime?.Date))
            return DateTimeOffset.Parse(eventTime.Date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

        return null;
    }

    private static CalendarEvent MapToDto(Event ev)
    {
        bool allDay = ev.Start?.Date != null;
        DateTimeOffset? start = ev.Start?.DateTimeDateTimeOffset ?? (ev.Start?.Date != null ? DateTimeOffset.Parse(ev.Start.Date, null, System.Globalization.DateTimeStyles.AssumeUniversal) : null);
        DateTimeOffset? end = ev.End?.DateTimeDateTimeOffset ?? (ev.End?.Date != null ? DateTimeOffset.Parse(ev.End.Date, null, System.Globalization.DateTimeStyles.AssumeUniversal) : null);

        var driveAttachments = MapAttachments(ev.Attachments);
        var fullAttendees = ev.Attendees?.Select(a => new CalendarEventAttendee(
            a.Email ?? string.Empty,
            a.DisplayName,
            a.ResponseStatus,
            a.Comment,
            a.Organizer ?? false
        )).ToList();

        var remindersList = new List<CalendarEventReminder>();
        if (ev.Reminders?.UseDefault == false && ev.Reminders.Overrides != null)
        {
            foreach (var r in ev.Reminders.Overrides)
            {
                remindersList.Add(new CalendarEventReminder(
                    r.Method ?? "popup",
                    r.Minutes ?? 0
                ));
            }
        }

        var recurrenceList = ev.Recurrence != null ? ev.Recurrence.ToList() : null;
        var selfAttendee = ev.Attendees?.FirstOrDefault(a => a.Self == true);
        var selfResponse = selfAttendee?.ResponseStatus ?? (ev.Organizer?.Self == true ? "accepted" : "needsAction");

        return new CalendarEvent(
            ev.Id,
            ev.ETag,
            ev.Summary,
            ev.Description,
            start,
            end,
            ev.Location,
            ev.Attendees?.Select(a => a.Email).ToList(),
            allDay,
            driveAttachments.Count > 0 ? driveAttachments : null,
            fullAttendees,
            ev.HangoutLink,
            ev.HtmlLink,
            remindersList.Count > 0 ? remindersList : null,
            recurrenceList,
            ev.Organizer?.Email,
            selfResponse,
            ev.ICalUID,
            ev.GuestsCanModify ?? false,
            ev.GuestsCanInviteOthers ?? true,
            ev.GuestsCanSeeOtherGuests ?? true);
    }

    private static readonly Regex DriveFileIdFromPathRegex = new(@"/d/([a-zA-Z0-9_-]+)", RegexOptions.Compiled);
    private static readonly Regex DriveFileIdFromOpenQueryRegex = new(@"[?&]id=([a-zA-Z0-9_-]+)", RegexOptions.Compiled);

    private static List<CalendarDriveAttachment> MapAttachments(IList<EventAttachment>? attachments)
    {
        if (attachments == null || attachments.Count == 0)
            return [];

        var result = new List<CalendarDriveAttachment>();
        foreach (var a in attachments)
        {
            var mapped = MapAttachment(a);
            if (mapped != null)
                result.Add(mapped);
        }

        return result;
    }

    private static CalendarDriveAttachment? MapAttachment(EventAttachment a)
    {
        var fileId = ResolveDriveFileId(a);
        if (string.IsNullOrEmpty(fileId) && string.IsNullOrEmpty(a.FileUrl))
            return null;

        return new CalendarDriveAttachment(fileId ?? string.Empty, a.Title, a.MimeType, a.FileUrl);
    }

    private static string? ResolveDriveFileId(EventAttachment a)
    {
        if (!string.IsNullOrEmpty(a.FileId))
            return a.FileId;

        if (string.IsNullOrEmpty(a.FileUrl))
            return null;

        var pathMatch = DriveFileIdFromPathRegex.Match(a.FileUrl);
        if (pathMatch.Success)
            return pathMatch.Groups[1].Value;

        var openMatch = DriveFileIdFromOpenQueryRegex.Match(a.FileUrl);
        return openMatch.Success ? openMatch.Groups[1].Value : null;
    }
}
