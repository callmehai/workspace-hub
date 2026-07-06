namespace WorkspaceHub.Application.DTOs.Sync;

/// <summary>
/// Kết quả sync 1 connection trong 1 lượt chạy cron.
/// Dùng trong ProcessSyncResult.Details để debug/log (optional trả về client).
/// </summary>

public record ConnectionSyncDetail(
    Guid ConnectionId,
    string ServiceType, //Gmail` / `GCal` / `Drive` / `Jira
    string Outcome,// "Success" | "Failed" | "Skipped"
    string? ErrorMessage = null,
    int Scanned = 0,
    int Created = 0,
    int Skipped = 0);
