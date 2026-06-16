using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Services;

public interface IDriveSyncService
{
    Task<SyncResult> SyncAsync(Guid connectionId, Guid userId, CancellationToken ct = default);
    Task<SyncResult> SyncConnectionAsync(Connection connection, CancellationToken ct = default);
}
