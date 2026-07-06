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

}