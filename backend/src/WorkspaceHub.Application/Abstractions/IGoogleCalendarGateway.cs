using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Abstractions;

public interface IGoogleCalendarGateway
{
    Task<CalendarSyncResult> SyncEventsAsync(Connection connection, string? syncToken, CancellationToken ct = default);
}
