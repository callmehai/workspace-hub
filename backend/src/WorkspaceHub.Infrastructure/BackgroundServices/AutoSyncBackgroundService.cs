using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Background service pull định kỳ (vài phút/lần) cho các ServiceConnection đang Active + Enabled.
/// </summary>
public class AutoSyncBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AutoSyncBackgroundService> _logger;

    public AutoSyncBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<AutoSyncBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutoSyncBackgroundService is starting.");

        // Chạy mỗi 5 phút (có thể đưa vào config)
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DoSyncWorkAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xảy ra trong quá trình AutoSyncBackgroundService loop.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("AutoSyncBackgroundService is stopping.");
    }

    private async Task DoSyncWorkAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutoSyncBackgroundService: Starting sync cycle.");

        using var scope = _serviceProvider.CreateScope();
        var connectionRepo = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
        var syncService = scope.ServiceProvider.GetRequiredService<IGmailSyncService>();
        
        var connections = await connectionRepo.GetActiveConnectionsToSyncAsync(stoppingToken);
        
        if (connections.Count == 0)
        {
            _logger.LogInformation("AutoSyncBackgroundService: No active connections to sync.");
            return;
        }

        foreach (var connection in connections)
        {
            if (stoppingToken.IsCancellationRequested) break;

            if (connection.ServiceType != ServiceType.Gmail)
            {
                _logger.LogWarning("AutoSyncBackgroundService: Skip sync for {ServiceType} (chưa hỗ trợ MVP). ConnectionId: {ConnectionId}", connection.ServiceType, connection.Id);
                continue;
            }

            _logger.LogInformation("AutoSyncBackgroundService: Syncing connection {ConnectionId} for User {UserId}", connection.Id, connection.UserId);

            try
            {
                var result = await syncService.SyncConnectionAsync(connection, 50, stoppingToken);
                _logger.LogInformation("AutoSyncBackgroundService: Synced connection {ConnectionId}. Scanned: {Scanned}, Created: {Created}, Skipped: {Skipped}", 
                    connection.Id, result.Scanned, result.Created, result.Skipped);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoSyncBackgroundService: Failed to sync connection {ConnectionId}", connection.Id);
                
                connection.LastError = ex.Message;
                // Nếu bị lỗi quota / 401, có thể set Status = Error tuỳ theo loại lỗi, nhưng mặc định ghi LastError
                connectionRepo.Update(connection);
                await connectionRepo.SaveChangesAsync(stoppingToken);
            }
        }
    }
}
