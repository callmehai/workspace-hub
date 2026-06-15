using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Application.Abstractions;

namespace WorkspaceHub.Application.Interfaces.Services;

public interface IGmailSyncService
{
    Task<SyncResult> SyncConnectionAsync(Connection connection, int maxMessages = 50, CancellationToken ct = default);
}
