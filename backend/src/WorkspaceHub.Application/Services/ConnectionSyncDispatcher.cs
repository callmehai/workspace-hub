using Google;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

public class ConnectionSyncDispatcher : IConnectionSyncDispatcher
{
    private readonly IConnectionRepository _connections;
    private readonly IItemRepository _items;
    private readonly ISyncItemNotificationService _syncNotifications;
    private readonly IGmailSyncService _gmailSync;
    private readonly ICalendarSyncService _calendarSync;
    private readonly IDriveSyncService _driveSync;
    private readonly IJiraSyncService _jiraSync;

    private const int GmailDefaultBatchSize = 50;
    private const int JiraDefaultBatchSize = 50;

    public ConnectionSyncDispatcher(
        IConnectionRepository connections,
        IItemRepository items,
        ISyncItemNotificationService syncNotifications,
        IGmailSyncService gmailSync,
        ICalendarSyncService calendarSync,
        IDriveSyncService driveSync,
        IJiraSyncService jiraSync)
    {
        _connections = connections;
        _items = items;
        _syncNotifications = syncNotifications;
        _gmailSync = gmailSync;
        _calendarSync = calendarSync;
        _driveSync = driveSync;
        _jiraSync = jiraSync;
    }

    public async Task<SyncResult> SyncAsync(Guid connectionId, Guid userId, CancellationToken ct = default)
    {
        var connection = await _connections.GetByIdAsync(connectionId, ct);
        if (connection is null || connection.UserId != userId)
            throw new NotFoundException("Connection", connectionId);

        try
        {
            var beforeExternalIds = await _items.GetExistingExternalIdsAsync(connectionId, ct);

            var result = connection.ServiceType switch
            {
                ServiceType.Gmail  => await _gmailSync.SyncConnectionAsync(connection, GmailDefaultBatchSize, ct),
                ServiceType.GCal   => await _calendarSync.SyncConnectionAsync(connection, ct),
                ServiceType.Drive  => await _driveSync.SyncConnectionAsync(connection, ct),
                ServiceType.Jira   => await _jiraSync.SyncConnectionAsync(connection, JiraDefaultBatchSize, ct),
                _ => throw new BusinessRuleException("Service type không hỗ trợ đồng bộ.")
            };

            if (result.Created > 0)
            {
                await _syncNotifications.NotifyNewItemsAsync(
                    connectionId, userId, beforeExternalIds, ct);
            }

            return result;
        }
        catch (GoogleApiException ex)
        {
            throw new ProviderException(
                $"Google API trả về lỗi {(int)ex.HttpStatusCode}: {ex.Error?.Message ?? ex.Message}",
                ex.HttpStatusCode,
                ex);
        }
    }
}
