using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Application.Common;

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

    private CalendarEvent MapToDto(Event ev)
    {
        DateTimeOffset? start = ev.Start?.DateTimeDateTimeOffset ?? (ev.Start?.Date != null ? DateTimeOffset.Parse(ev.Start.Date, null, System.Globalization.DateTimeStyles.AssumeUniversal) : null);
        DateTimeOffset? end = ev.End?.DateTimeDateTimeOffset ?? (ev.End?.Date != null ? DateTimeOffset.Parse(ev.End.Date, null, System.Globalization.DateTimeStyles.AssumeUniversal) : null);

        return new CalendarEvent(
            ev.Id,
            ev.ETag,
            ev.Summary,
            ev.Description,
            start,
            end,
            ev.Location,
            ev.Attendees?.Select(a => a.Email).ToList());
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
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new NotFoundException("Event", eventId);
            }
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden || (ex.Error != null && ex.Error.Errors != null && ex.Error.Errors.Any(e => e.Reason != null && e.Reason.Contains("insufficientPermissions", StringComparison.OrdinalIgnoreCase))))
            {
                throw new ForbiddenException("Cần reconnect.");
            }
            throw new ProviderException($"Calendar API error: {ex.Message}");
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
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound) throw new NotFoundException("Event", eventId);
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden || (ex.Error != null && ex.Error.Errors != null && ex.Error.Errors.Any(e => e.Reason != null && e.Reason.Contains("insufficientPermissions", StringComparison.OrdinalIgnoreCase))))
            {
                throw new ForbiddenException("Cần reconnect với quyền ghi.");
            }
            throw new ProviderException($"Calendar API error: {ex.Message}");
        }
    }

    public async Task<CalendarEvent> InsertEventAsync(Connection connection, string calendarId, CalendarEvent eventDto, CancellationToken ct = default)
    {
        try
        {
            using var calendar = await BuildCalendarServiceAsync(connection, ct);
            
            var ev = new Event
            {
                Summary = eventDto.Summary,
                Description = eventDto.Description,
                Location = eventDto.Location,
                Attendees = eventDto.Attendees?.Select(a => new EventAttendee { Email = a }).ToList(),
                Start = eventDto.Start.HasValue ? new EventDateTime { DateTimeDateTimeOffset = eventDto.Start.Value } : null,
                End = eventDto.End.HasValue ? new EventDateTime { DateTimeDateTimeOffset = eventDto.End.Value } : null
            };
            
            var created = await calendar.Events.Insert(ev, calendarId).ExecuteAsync(ct);
            return MapToDto(created);
        }
        catch (Google.GoogleApiException ex)
        {
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound) throw new NotFoundException("Event", calendarId);
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden || (ex.Error != null && ex.Error.Errors != null && ex.Error.Errors.Any(e => e.Reason != null && e.Reason.Contains("insufficientPermissions", StringComparison.OrdinalIgnoreCase))))
            {
                throw new ForbiddenException("Cần reconnect với quyền ghi.");
            }
            throw new ProviderException($"Calendar API error: {ex.Message}");
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
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound) throw new NotFoundException("Event", eventId);
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden || (ex.Error != null && ex.Error.Errors != null && ex.Error.Errors.Any(e => e.Reason != null && e.Reason.Contains("insufficientPermissions", StringComparison.OrdinalIgnoreCase))))
            {
                throw new ForbiddenException("Cần reconnect với quyền ghi.");
            }
            throw new ProviderException($"Calendar API error: {ex.Message}");
        }
    }
}

