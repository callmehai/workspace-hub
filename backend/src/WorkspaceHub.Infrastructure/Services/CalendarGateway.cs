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
            
            var existing = new Event();
            if (eventDto.Summary != null) existing.Summary = eventDto.Summary;
            if (eventDto.Description != null) existing.Description = eventDto.Description;
            if (eventDto.Location != null) existing.Location = eventDto.Location;
            if (eventDto.Attendees != null) existing.Attendees = eventDto.Attendees.Select(a => new EventAttendee { Email = a }).ToList();
            if (eventDto.Start.HasValue) existing.Start = new EventDateTime { DateTimeDateTimeOffset = eventDto.Start.Value };
            if (eventDto.End.HasValue) existing.End = new EventDateTime { DateTimeDateTimeOffset = eventDto.End.Value };
            
            var request = calendar.Events.Patch(existing, calendarId, eventId);
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
                    Start = eventDto.Start.HasValue ? new EventDateTime { DateTimeDateTimeOffset = eventDto.Start.Value } : null,
                    End = eventDto.End.HasValue ? new EventDateTime { DateTimeDateTimeOffset = eventDto.End.Value } : null
                };
            }

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

            var insertRequest = calendar.Events.Insert(ev, calendarId);
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
            await calendar.Events.Delete(calendarId, eventId).ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Calendar", "Event", eventId);
        }
    }

    // ───────────────────────── Private helpers ─────────────────────────

    private static CalendarEvent MapToDto(Event ev)
    {
        bool allDay = ev.Start?.Date != null;
        DateTimeOffset? start = ev.Start?.DateTimeDateTimeOffset ?? (ev.Start?.Date != null ? DateTimeOffset.Parse(ev.Start.Date, null, System.Globalization.DateTimeStyles.AssumeUniversal) : null);
        DateTimeOffset? end = ev.End?.DateTimeDateTimeOffset ?? (ev.End?.Date != null ? DateTimeOffset.Parse(ev.End.Date, null, System.Globalization.DateTimeStyles.AssumeUniversal) : null);

        var driveAttachments = ev.Attachments?.Select(a => new CalendarDriveAttachment(
            a.FileId ?? "",
            a.Title,
            a.MimeType,
            a.FileUrl)).ToList();

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
            driveAttachments?.Count > 0 ? driveAttachments : null);
    }
}
