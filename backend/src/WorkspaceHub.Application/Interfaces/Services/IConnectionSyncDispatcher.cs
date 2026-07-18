using WorkspaceHub.Application.Abstractions;

namespace WorkspaceHub.Application.Interfaces.Services;

public interface IConnectionSyncDispatcher
{
    /// <param name="markProviderError">
    /// true = user-initiated sync (Integrations/Toolbar): đánh Status=Error khi provider fail
    /// để UI hiện «lỗi đồng bộ» ngay. Cron/on-demand để false — cron tự phân loại auth vs transient.
    /// </param>
    Task<SyncResult> SyncAsync(
        Guid connectionId,
        Guid userId,
        CancellationToken ct = default,
        bool markProviderError = false);
}
