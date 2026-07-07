namespace WorkspaceHub.Application.DTOs.Sync;

/// <summary>
/// Kết quả 1 lượt chạy cron POST /api/internal/process-sync. khác với ConnectionSyncDetail, vì ProcessSyncResult trả về cho cron ngoài log/monitor, còn ConnectionSyncDetail trả về cho client.
/// Mirror pattern ProcessScheduledResult (SCRUM-31) — trả về cho cron ngoài log/monitor.
/// </summary>

public record ProcessSyncResult(
    int TotalConnections, //Số connection Active + integration enabled được quét
    int SuccessCount, //số lượng sync thành công
    int SkippedCount,//Bị debounce (vừa sync gần đây) — không gọi provider
    int ErrorCount,
    IReadOnlyList<ConnectionSyncDetail>? Details = null //Chi tiết từng connection, dùng để debug/log (optional trả về client)
);