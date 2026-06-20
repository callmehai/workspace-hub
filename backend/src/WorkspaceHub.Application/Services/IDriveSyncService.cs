using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Services;

public interface IDriveSyncService
{
    Task<SyncResult> SyncConnectionAsync(Connection connection, CancellationToken ct = default);
}
