using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Abstractions;

public interface IDriveGateway
{
    Task<DriveFile> GetFileAsync(Connection connection, string fileId, CancellationToken ct = default);
    Task<DriveFile> UpdateFileAsync(Connection connection, string fileId, string newName, CancellationToken ct = default);
    Task<DriveFile> TrashFileAsync(Connection connection, string fileId, CancellationToken ct = default);
    Task<DriveFile> UntrashFileAsync(Connection connection, string fileId, CancellationToken ct = default);

    //Tạo folder trên Google Drive. parentExternalId null = My Drive root.
    Task<DriveFileDto> CreateFolderAsync(
        Connection connection,
        string name,
        string?parentExternalId,
        CancellationToken ct= default);

    // Danh sách quyền share của file/folder trên Google Drive.
    Task<IReadOnlyList<DrivePermissionDto>> ListPermissionAsync(
        Connection connection,
        string fileId,
        CancellationToken ct = default);

    // Tạo quyền share cho user (email) trên file/folder Google Drive.
    Task<DrivePermissionDto> CreateUserPermissionAsync(
        Connection connection,
        string fileId,
        string email,
        DrivePermissionRole role,
        bool notify,
        CancellationToken ct = default);

    //Update quyền share (role) cho user trên file/folder Google Drive.
    Task<DrivePermissionDto> UpdatePermissionAsync(
        Connection connection,
        string fileId,
        string permissionId,
        DrivePermissionRole role,
        CancellationToken ct = default);

    //Xóa quyền share (permissionId) trên file/folder Google Drive.
    Task DeletePermissionAsync(
    Connection connection,
    string fileId,
    string permissionId,
    CancellationToken ct = default);

    // Bật/tắt link share (anyone with link) trên file/folder Google Drive.
    Task<DrivePermissionDto?> SetLinkSharingAsync(
        Connection connection,
        string fileId,
        bool enable,
        DrivePermissionRole role,
        CancellationToken ct = default);
}
