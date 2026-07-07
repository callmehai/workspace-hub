using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Sync;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Infrastructure.Services;

public class ProcessConnectionsSyncService: IProcessConnectionsSyncService{
    private readonly IConnectionRepository _connections;
    private readonly IConnectionSyncDispatcher _syncDispatcher;
    private readonly IConnectionsService _connectionsService;
    private readonly ILogger<ProcessConnectionsSyncService> _logger;
    private readonly int _debounceSeconds;

     public ProcessConnectionsSyncService(
        IConnectionRepository connections,
        IConnectionSyncDispatcher syncDispatcher,
        IConnectionsService connectionsService,
        ILogger<ProcessConnectionsSyncService> logger,
        IConfiguration config)
    {
        _connections = connections;
        _syncDispatcher = syncDispatcher;
        _connectionsService = connectionsService;
        _logger = logger;
        _debounceSeconds = config.GetValue("Sync:DebounceSeconds", 30);
    }

    public async Task<ProcessSyncResult> ProcessConnectionsSyncAsync( CancellationToken cancellationToken = default){
        //lấy danh sách connection active + integration enabled, check debounce (vừa sync gần đây) → sync từng cái, thống kê kết quả.
        var connections = await _connections.GetActiveConnectionsToSyncAsync(cancellationToken);
        if(connections.Count == 0){
            _logger.LogInformation("No active connections to sync.");
            return new ProcessSyncResult(0, 0, 0, 0);
        }
        var details = new List<ConnectionSyncDetail>();
        var successCount = 0;
        var skippedCount = 0;
        var errorCount = 0;

        foreach (var conn in connections)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var detail = await ProcessSingleAsync(conn.Id, conn.UserId, cancellationToken);
                details.Add(detail);

                switch (detail.Outcome)
                {
                    case "Success": successCount++; break;
                    case "Skipped": skippedCount++; break;
                    default: errorCount++; break;
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                details.Add(new ConnectionSyncDetail(
                    conn.Id, conn.ServiceType.ToString(), "Error", ex.Message));

                _logger.LogWarning(ex,
                    "Cron sync failed for connection {ConnectionId}, continuing.", conn.Id);
            }
        }

        _logger.LogInformation(
            "Cron sync finished. Total={Total}, Success={Success}, Skipped={Skipped}, Error={Error}",
            connections.Count, successCount, skippedCount, errorCount);

        return new ProcessSyncResult(
            connections.Count, successCount, skippedCount, errorCount, details);
    }

    private async Task<ConnectionSyncDetail> ProcessSingleAsync(Guid connectionId, Guid userId, CancellationToken ct)
    {
        var conn = await _connections.GetByIdTrackedAsync(connectionId, ct);
        if (conn is null || conn.Status != ConnectionStatus.Active)
        {
            return new ConnectionSyncDetail(
                connectionId, conn?.ServiceType.ToString() ?? "Unknown", "Error",
                "Connection not found or not active.");
        }

        //Nếu connection vừa sync gần đây (LastSyncedAt < debounceSeconds) thì skip, không gọi provider.
        //debaunceSeconds là thời gian tối thiểu giữa 2 lần sync liên tiếp của 1 connection, để tránh spam provider.
        if (conn.LastSyncedAt.HasValue &&
            (DateTime.UtcNow - conn.LastSyncedAt.Value).TotalSeconds < _debounceSeconds)
        {
            return new ConnectionSyncDetail(connectionId, conn.ServiceType.ToString(), "Skipped");
        }

        //Nếu token sắp hết hạn (< 5 phút) thì refresh token trước khi sync.
        if (conn.ExpiresAt < DateTime.UtcNow.AddMinutes(5))
        {
            try
            {
                // refresh token xong thì lấy lại connection tracked để sync.
                await _connectionsService.RefreshConnectionAsync(connectionId, userId, ct);
                // refresh token xong thì lấy lại connection tracked để sync.
                conn = await _connections.GetByIdTrackedAsync(connectionId, ct);
                if (conn is null || conn.Status != ConnectionStatus.Active)
                {
                    return new ConnectionSyncDetail(
                        connectionId, conn?.ServiceType.ToString() ?? "Unknown", "Error",
                        "Connection unavailable after token refresh.");
                }
            }
            catch (Exception ex)
            {
                await MarkConnectionErrorAsync(connectionId,
                    $"Token expired and auto-refresh failed. Error: {ex.Message}", ct);
                return new ConnectionSyncDetail(
                    connectionId, conn?.ServiceType.ToString() ?? "Unknown", "Error", ex.Message);
            }
        }
        try
        {
            var result = await _syncDispatcher.SyncAsync(connectionId, userId, ct);

            _logger.LogInformation(
                "Cron sync OK for {ConnectionId}. Scanned={Scanned}, Created={Created}, Skipped={Skipped}",
                connectionId, result.Scanned, result.Created, result.Skipped);

            return new ConnectionSyncDetail(
                connectionId, conn.ServiceType.ToString(), "Success",
                null, result.Scanned, result.Created, result.Skipped);
        }
        catch (Exception ex)
        {
            if (IsPersistentAuthFailure(ex))
                await MarkConnectionErrorAsync(connectionId, ex.Message, ct);
            else
                _logger.LogWarning(ex,
                    "Transient cron sync failure for {ConnectionId}; keeping Active for retry.",
                    connectionId);

            return new ConnectionSyncDetail(
                connectionId, conn.ServiceType.ToString(), "Error", ex.Message);
        }
    }

    private static bool IsPersistentAuthFailure(Exception ex)
        => ex is ForbiddenException or UnauthorizedException;

    private async Task MarkConnectionErrorAsync(Guid connectionId, string message, CancellationToken ct)
    {
        //lấy connection tracked để update LastSyncAt, LastSyncError, LastSyncOutcome
        var conn = await _connections.GetByIdTrackedAsync(connectionId, ct);
        if (conn is null) return;

        conn.Status = ConnectionStatus.Error;
        conn.LastError = message;
        _connections.Update(conn);
        await _connections.SaveChangesAsync(ct);
    }
}