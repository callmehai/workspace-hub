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

            if (eventDto.Summary != null) existing.Summary = eventDto.Summary;
            if (eventDto.Description != null) existing.Description = eventDto.Description;
            if (eventDto.Location != null) existing.Location = eventDto.Location;
            if (eventDto.Attendees != null)
                existing.Attendees = eventDto.Attendees.Select(a => new EventAttendee { Email = a }).ToList();

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

            var request = calendar.Events.Update(existing, calendarId, eventId);
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
            driveAttachments.Count > 0 ? driveAttachments : null);
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
