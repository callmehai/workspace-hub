using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Abstractions;

public interface ICalendarGateway
{
    Task<CalendarSyncResult> SyncEventsAsync(Connection connection, string? syncToken, CancellationToken ct = default);
    Task<CalendarEvent> GetEventAsync(Connection connection, string calendarId, string eventId, CancellationToken ct = default);
    Task<CalendarEvent> UpdateEventAsync(Connection connection, string calendarId, string eventId, CalendarEvent eventDto, CancellationToken ct = default);
    Task<CalendarEvent> InsertEventAsync(Connection connection, string calendarId, CalendarEvent eventDto, CancellationToken ct = default);
    Task DeleteEventAsync(Connection connection, string calendarId, string eventId, CancellationToken ct = default);
    Task RsvpEventAsync(Connection connection, string calendarId, string eventId, string responseStatus, string? comment, CancellationToken ct = default);
    Task<CalendarEvent?> FindEventByICalUidAsync(Connection connection, string calendarId, string iCalUid, CancellationToken ct = default);
}
