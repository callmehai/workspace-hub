using WorkspaceHub.Application.DTOs.ScheduledEmails;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// Xử lý batch email hẹn giờ tới hạn (SCRUM-31). Cron ngoài gọi qua
/// POST /api/internal/process-scheduled mỗi ~5 phút (bảo vệ bằng X-Cron-Secret).
/// </summary>
public interface IProcessScheduledEmailsService
{
    /// <summary>
    /// Gửi mọi email Pending có SendAt &lt;= now qua Gmail. Mỗi email thành công → Sent,
    /// lỗi → Failed (RetryCount++, LastError). Trả về thống kê lượt chạy.
    /// </summary>
    Task<ProcessScheduledResult> ProcessDueEmailsAsync(int maxBatch = 50, CancellationToken ct = default);
}
