using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Abstractions;

public interface IDriveGateway
{
    Task<DriveFile> GetFileAsync(Connection connection, string fileId, CancellationToken ct = default);
    Task<DriveFile> UpdateFileAsync(Connection connection, string fileId, string newName, CancellationToken ct = default);
    Task<DriveFile> TrashFileAsync(Connection connection, string fileId, CancellationToken ct = default);
    Task<DriveFile> UntrashFileAsync(Connection connection, string fileId, CancellationToken ct = default);
}
