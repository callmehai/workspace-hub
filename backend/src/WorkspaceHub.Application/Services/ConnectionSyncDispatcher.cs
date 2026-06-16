using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

public class ConnectionSyncDispatcher : IConnectionSyncDispatcher
{
    private readonly IConnectionRepository _connections;
    private readonly IGmailSyncService _gmailSync;
    private readonly ICalendarSyncService _calendarSync;
    private readonly IDriveSyncService _driveSync;

    public ConnectionSyncDispatcher(
        IConnectionRepository connections,
        IGmailSyncService gmailSync,
        ICalendarSyncService calendarSync,
        IDriveSyncService driveSync)
    {
        _connections = connections;
        _gmailSync = gmailSync;
        _calendarSync = calendarSync;
        _driveSync = driveSync;
    }

    public async Task<SyncResult> SyncAsync(Guid connectionId, Guid userId, CancellationToken ct = default)
    {
        var connection = await _connections.GetByIdAsync(connectionId, ct);
        if (connection is null || connection.UserId != userId)
            throw new NotFoundException("Connection", connectionId);

        return connection.ServiceType switch
        {
            ServiceType.Gmail => await _gmailSync.SyncConnectionAsync(connection, 50, ct),
            ServiceType.GCal => await _calendarSync.SyncConnectionAsync(connection, ct),
            ServiceType.Drive => await _driveSync.SyncConnectionAsync(connection, ct),
            _ => throw new BusinessRuleException("Service type không hỗ trợ đồng bộ.")
        };
    }
}
