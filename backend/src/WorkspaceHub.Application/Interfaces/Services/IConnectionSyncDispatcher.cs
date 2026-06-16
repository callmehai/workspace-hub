using WorkspaceHub.Application.Abstractions;

namespace WorkspaceHub.Application.Interfaces.Services;

public interface IConnectionSyncDispatcher
{
    Task<SyncResult> SyncAsync(Guid connectionId, Guid userId, CancellationToken ct = default);
}
