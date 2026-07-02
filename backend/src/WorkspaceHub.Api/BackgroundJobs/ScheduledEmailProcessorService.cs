using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.BackgroundJobs;

/// <summary>
/// Tự động quét &amp; gửi scheduled email tới hạn mỗi <c>Cron:IntervalSeconds</c> (mặc định 300s = 5 phút).
///
/// CHỈ đăng ký khi <c>Cron:AutoRun=true</c> (xem Program.cs). Mặc định prod = false — theo thiết kế
/// gốc (CLAUDE.md: "BE không tự hẹn giờ", cron ngoài gọi <c>/api/internal/process-scheduled</c>).
/// Dev bật để tiện test. KHÔNG thay thế endpoint — endpoint vẫn dùng cho cron ngoài.
///
/// BackgroundService là singleton nên phải tự tạo scope để resolve IProcessScheduledEmailsService (scoped).
/// </summary>
public class ScheduledEmailProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledEmailProcessorService> _logger;
    private readonly TimeSpan _interval;

    public ScheduledEmailProcessorService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<ScheduledEmailProcessorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var seconds = config.GetValue<int?>("Cron:IntervalSeconds") ?? 300;
        if (seconds < 10) seconds = 10; // guard tránh spam nếu cấu hình nhầm
        _interval = TimeSpan.FromSeconds(seconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ScheduledEmailProcessor started — quét mỗi {Interval}s.", _interval.TotalSeconds);

        using var timer = new PeriodicTimer(_interval);
        try
        {
            // Chạy ngay 1 lần lúc khởi động, rồi lặp theo timer.
            do
            {
                await ProcessOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // App đang tắt — thoát êm.
        }
    }

    private async Task ProcessOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IProcessScheduledEmailsService>();
            var result = await processor.ProcessDueEmailsAsync(ct: ct);

            if (result.Total > 0)
            {
                _logger.LogInformation(
                    "Auto-cron: xử lý {Total} email — {Sent} gửi, {Failed} lỗi.",
                    result.Total, result.Sent, result.Failed);
            }
        }
        catch (OperationCanceledException)
        {
            throw; // để ExecuteAsync thoát êm khi shutdown
        }
        catch (Exception ex)
        {
            // Nuốt lỗi để vòng lặp không chết — lần tick sau thử lại.
            _logger.LogError(ex, "Auto-cron: lỗi khi xử lý scheduled email, sẽ thử lại lần sau.");
        }
    }
}
