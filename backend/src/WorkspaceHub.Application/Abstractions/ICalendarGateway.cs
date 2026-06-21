using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Abstractions;

public interface ICalendarGateway
{
    Task<CalendarEvent> GetEventAsync(Connection connection, string calendarId, string eventId, CancellationToken ct = default);
    Task<CalendarEvent> UpdateEventAsync(Connection connection, string calendarId, string eventId, CalendarEvent eventDto, CancellationToken ct = default);
    Task<CalendarEvent> InsertEventAsync(Connection connection, string calendarId, CalendarEvent eventDto, CancellationToken ct = default);
    Task DeleteEventAsync(Connection connection, string calendarId, string eventId, CancellationToken ct = default);
}
