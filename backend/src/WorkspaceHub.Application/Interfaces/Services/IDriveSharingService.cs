using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// Business logic tạo folder Drive + chia sẻ permissions (SCRUM-79 — A3).
/// Controller gọi service này; service validate ownership rồi ủy thác Google cho <see cref="Abstractions.IDriveGateway"/>.
/// </summary>
public interface IDriveSharingService
{
    /// <summary>Tạo folder trên Google Drive và insert Item local ngay (không chờ cron sync).</summary>
    Task<ItemResponse> CreateFolderAsync(
        Guid userId,
        Guid connectionId,
        string name,
        Guid? parentItemId = null,
        CancellationToken ct = default);

    /// <summary>Danh sách quyền share của file/folder Drive.</summary>
    Task<DrivePermissionsListResponse> ListPermissionsAsync(
        Guid userId,
        Guid itemId,
        CancellationToken ct = default);

    /// <summary>Thêm quyền share cho email.</summary>
    Task<DrivePermissionDto> AddPermissionAsync(
        Guid userId,
        Guid itemId,
        string email,
        DrivePermissionRole role,
        bool notify = true,
        CancellationToken ct = default);

    /// <summary>Đổi role permission (không sửa owner).</summary>
    Task<DrivePermissionDto> UpdatePermissionAsync(
        Guid userId,
        Guid itemId,
        string permissionId,
        DrivePermissionRole role,
        CancellationToken ct = default);

    /// <summary>Gỡ permission (không xoá owner).</summary>
    Task RemovePermissionAsync(
        Guid userId,
        Guid itemId,
        string permissionId,
        CancellationToken ct = default);

    /// <summary>Bật/tắt link "ai có đường link".</summary>
    Task<DrivePermissionDto?> SetLinkSharingAsync(
        Guid userId,
        Guid itemId,
        bool enabled,
        DrivePermissionRole role = DrivePermissionRole.Reader,
        CancellationToken ct = default);
}

/// <summary>Response GET /api/drive/items/{itemId}/permissions.</summary>
public record DrivePermissionsListResponse(IReadOnlyList<DrivePermissionDto> Items);
