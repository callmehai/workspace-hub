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

    private const int GmailDefaultBatchSize = 100;
    // Kéo toàn bộ board (không chỉ việc của mình) → nâng trần để không cụt danh sách ticket.
    private const int JiraDefaultBatchSize = 250;

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

    public async Task<SyncResult> SyncAsync(
        Guid connectionId,
        Guid userId,
        CancellationToken ct = default,
        bool markProviderError = false)
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
            var providerEx = new ProviderException(
                $"Google API trả về lỗi {(int)ex.HttpStatusCode}: {ex.Error?.Message ?? ex.Message}",
                ex.HttpStatusCode,
                ex);
            if (markProviderError)
                await MarkConnectionErrorAsync(connectionId, providerEx.Message, ct);
            throw providerEx;
        }
        catch (ProviderException ex)
        {
            // Jira (và gateway khác) throw ProviderException trực tiếp → 502 + Error trên UI khi sync tay.
            if (markProviderError)
                await MarkConnectionErrorAsync(connectionId, ex.Message, ct);
            throw;
        }
    }

    private async Task MarkConnectionErrorAsync(Guid connectionId, string message, CancellationToken ct)
    {
        var tracked = await _connections.GetByIdTrackedAsync(connectionId, ct);
        if (tracked is null) return;

        tracked.Status = ConnectionStatus.Error;
        tracked.LastError = message;
        _connections.Update(tracked);
        await _connections.SaveChangesAsync(ct);
    }
}
