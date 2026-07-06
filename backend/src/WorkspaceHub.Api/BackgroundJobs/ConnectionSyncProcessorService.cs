using WorkspaceHub.Application.Interfaces.Services;
namespace WorkspaceHub.Api.BackgroundJobs;

/// <summary>
/// Tự đồng sync connections mỗi Cron:SyncIntervalSeconds (mặc định 300s).
/// CHỈ đăng ký khi Cron:SyncAutoRun=true. Prod dùng cron ngoài gọi /api/internal/process-sync.
/// </summary>  
public class ConnectionSyncProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConnectionSyncProcessorService> _logger;
    private readonly TimeSpan _interval;


    public ConnectionSyncProcessorService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<ConnectionSyncProcessorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var seconds = config.GetValue<int?>("Cron:SyncIntervalSeconds") ?? 300;
        if (seconds < 10) seconds = 10;
        _interval = TimeSpan.FromSeconds(seconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ConnectionSyncProcessor started — quét mỗi {Interval}s.", _interval.TotalSeconds);

        using var timer = new PeriodicTimer(_interval);
        try
        {
            do { await ProcessOnceAsync(stoppingToken); }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { }
    }

    private async Task ProcessOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IProcessConnectionsSyncService>();
            var result = await processor.ProcessConnectionsSyncAsync(ct);

            if (result.TotalConnections > 0)
            {
                _logger.LogInformation(
                    "Auto-cron sync: {Total} connections — {Success} OK, {Skipped} skipped, {Error} errors.",
                    result.TotalConnections, result.SuccessCount, result.SkippedCount, result.ErrorCount);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-cron sync failed, will retry next tick.");
        }
    }
}