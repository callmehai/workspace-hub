using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

/// <summary>
/// On-demand sync: check connection health, auto-refresh token, sync nếu cần.
/// Mỗi connection lỗi KHÔNG chặn connection khác.
/// </summary>
public class ConnectionHealthChecker : IConnectionHealthChecker
{
    private readonly IConnectionRepository _connections;
    private readonly IConnectionsService _connectionsService;
    private readonly IGmailSyncService _syncService;
    private readonly ILogger<ConnectionHealthChecker> _logger;
    private readonly int _debounceSeconds;

    public ConnectionHealthChecker(
        IConnectionRepository connections,
        IConnectionsService connectionsService,
        IGmailSyncService syncService,
        ILogger<ConnectionHealthChecker> logger,
        IConfiguration config)
    {
        _connections = connections;
        _connectionsService = connectionsService;
        _syncService = syncService;
        _logger = logger;
        _debounceSeconds = config.GetValue("Sync:DebounceSeconds", 30);
    }

    public async Task EnsureAllSyncedAsync(Guid userId, CancellationToken ct = default)
    {
        var allConnections = await _connections.GetActiveConnectionsForUserAsync(userId, ct);

        if (allConnections.Count == 0)
        {
            return; // Không có connection nào → không làm gì
        }

        foreach (var conn in allConnections)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await EnsureSingleConnectionSyncedAsync(conn.Id, userId, ct);
            }
            catch (Exception ex)
            {
                // Một connection lỗi KHÔNG chặn các connection khác
                _logger.LogWarning(ex,
                    "On-demand sync failed for connection {ConnectionId}, continuing with others.", conn.Id);
            }
        }
    }

    private async Task EnsureSingleConnectionSyncedAsync(Guid connectionId, Guid userId, CancellationToken ct)
    {
        var conn = await _connections.GetByIdTrackedAsync(connectionId, ct);
        if (conn == null) return;

        // Chỉ sync connection Active + Integration Enabled
        if (conn.Status != ConnectionStatus.Active) return;

        // Debounce: nếu vừa sync trong X giây trước → bỏ qua
        if (conn.LastSyncedAt.HasValue &&
            (DateTime.UtcNow - conn.LastSyncedAt.Value).TotalSeconds < _debounceSeconds)
        {
            return;
        }

        // Check token expiry → auto-refresh nếu cần
        if (conn.ExpiresAt < DateTime.UtcNow.AddMinutes(5))
        {
            try
            {
                await _connectionsService.RefreshConnectionAsync(connectionId, userId, ct);
                // Re-fetch connection sau khi refresh (token mới)
                conn = await _connections.GetByIdTrackedAsync(connectionId, ct);
                if (conn == null || conn.Status != ConnectionStatus.Active) return;
            }
            catch (Exception ex)
            {
                // Refresh fail (revoked/invalid) → đánh dấu connection cần re-auth
                conn!.Status = ConnectionStatus.Error;
                conn.LastError = $"Token expired and auto-refresh failed. Please re-authenticate. Error: {ex.Message}";
                _connections.Update(conn);
                await _connections.SaveChangesAsync(ct);

                _logger.LogWarning(ex,
                    "Auto-refresh failed for connection {ConnectionId}. Connection needs re-auth.", connectionId);
                return; // Không sync, nhưng KHÔNG chặn các connection khác
            }
        }

        // Chỉ hỗ trợ Gmail hiện tại
        if (conn.ServiceType != ServiceType.Gmail)
        {
            return;
        }

        // Sync
        var result = await _syncService.SyncConnectionAsync(conn, 50, ct);
        _logger.LogInformation(
            "On-demand sync completed for connection {ConnectionId}. Scanned: {Scanned}, Created: {Created}, Skipped: {Skipped}",
            connectionId, result.Scanned, result.Created, result.Skipped);
    }
}
