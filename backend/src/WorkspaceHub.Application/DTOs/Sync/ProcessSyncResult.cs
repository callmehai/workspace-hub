namespace WorkspaceHub.Application.DTOs.Sync;

/// <summary>
/// Kết quả 1 lượt chạy cron POST /api/internal/process-sync. khác với ConnectionSyncDetail, vì ProcessSyncResult trả về cho cron ngoài log/monitor, còn ConnectionSyncDetail trả về cho client.
/// Mirror pattern ProcessScheduledResult (SCRUM-31) — trả về cho cron ngoài log/monitor.
/// </summary>

public record ProcessSyncResult(
    int TottalConnections,
    int SuccessCount,
    int SkippedCount,
    int ErrorCount,
    IReadOnlyList<ConnectionSyncDetail>? Details = null
);