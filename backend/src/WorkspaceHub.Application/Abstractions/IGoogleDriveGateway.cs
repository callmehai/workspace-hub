using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Abstractions;

public interface IGoogleDriveGateway
{
    Task<DriveSyncResult> SyncFilesAsync(Connection connection, string? pageToken, CancellationToken ct = default);
}
