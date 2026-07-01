namespace WorkspaceHub.Application.DTOs.ScheduledEmails;

/// <summary>
/// Kết quả 1 lượt chạy cron /process-scheduled (SCRUM-31).
/// Total = số email tới hạn được xử lý; Sent/Failed = phân loại kết quả.
/// </summary>
public record ProcessScheduledResult(int Total, int Sent, int Failed);
