using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Application.Abstractions;

namespace WorkspaceHub.Application.Interfaces.Services;

public interface IGmailSyncService
{
    Task<GmailProfile> GetProfileAsync(Guid connectionId, Guid userId, CancellationToken ct = default);
    Task<GmailSampleDto> GetSampleAsync(Guid connectionId, Guid userId, CancellationToken ct = default);
    Task<SyncResult> SyncConnectionAsync(Connection connection, int maxMessages = 50, CancellationToken ct = default);
}
