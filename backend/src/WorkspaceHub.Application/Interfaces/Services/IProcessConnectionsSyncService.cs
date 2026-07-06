using WorkspaceHub.Application.DTOs.Sync;
namespace WorkspaceHub.Application.Interfaces.Services;

public interface IProcessConnectionsSyncService{
     /// <summary>
    /// Quét mọi Connection Active + Integration enabled → sync từng cái.
    /// Mỗi connection lỗi KHÔNG chặn connection khác. Trả thống kê lượt chạy.
    /// </summary>
    Task<ProcessSyncResult> ProcessConnectionsSyncAsync(CancellationToken cancellationToken = default);
}