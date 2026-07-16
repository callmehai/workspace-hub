using System.Text.Json;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.DTOs.Drive;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

/// <summary>
/// Orchestrate tạo folder + chia sẻ Drive (SCRUM-79 — A3).
/// Biết User/Item/Connection của app; gọi Google qua <see cref="IDriveGateway"/>.
/// </summary>
public class DriveSharingService : IDriveSharingService
{
    private readonly IDriveGateway _gateway;
    private readonly IItemRepository _items;
    private readonly IConnectionRepository _connections;
    private readonly IDriveItemMapper _mapper;

    public DriveSharingService(
        IDriveGateway gateway,
        IItemRepository items,
        IConnectionRepository connections,
        IDriveItemMapper mapper)
    {
        _gateway = gateway;
        _items = items;
        _connections = connections;
        _mapper = mapper;
    }

    public async Task<ItemResponse> CreateFolderAsync(
        Guid userId,
        Guid connectionId,
        string name,
        Guid? parentItemId = null,
        CancellationToken ct = default)
    {
        ValidateFolderName(name);
        var conn = await GetValidDriveConnectionAsync(connectionId, userId, ct);

        string? parentExternalId = null;
        if (parentItemId.HasValue)
        {
            var parent = await _items.GetByIdAndUserAsync(parentItemId.Value, userId, ct)
                ?? throw new NotFoundException("Item", parentItemId.Value);

            if (parent.ConnectionId != connectionId)
                throw new BusinessRuleException("Folder cha phải thuộc cùng connection Drive.");

            if (!IsDriveFolder(parent))
                throw new BusinessRuleException("parentItemId phải trỏ tới folder Drive.");

            if (parent.IsArchived)
                throw new BusinessRuleException("Không thể tạo folder con trong folder đã trash.");

            parentExternalId = parent.ExternalId;
        }

        var driveFile = await _gateway.CreateFolderAsync(conn, name.Trim(), parentExternalId, ct);

        var item = _mapper.ToItem(driveFile, userId, connectionId);

        await _items.AddAsync(item, ct);
        await _items.SaveChangesAsync(ct);

        return MapToItemResponse(item);
    }

    public async Task<DrivePermissionsListResponse> ListPermissionsAsync(
        Guid userId,
        Guid itemId,
        CancellationToken ct = default)
    {
        var (item, conn) = await ResolveDriveItemAsync(itemId, userId, ct);
        var list = await _gateway.ListPermissionsAsync(conn, item.ExternalId!, ct);
        return new DrivePermissionsListResponse(list);
    }

    public async Task<DrivePermissionDto> AddPermissionAsync(
        Guid userId,
        Guid itemId,
        string email,
        DrivePermissionRole role,
        bool notify = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new BusinessRuleException("Email là bắt buộc.");

        var (item, conn) = await ResolveDriveItemAsync(itemId, userId, ct);

        var existing = await _gateway.ListPermissionsAsync(conn, item.ExternalId!, ct);
        if (existing.Any(p =>
                string.Equals(p.Type, DrivePermissionTypes.User, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.EmailAddress, email.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("Email này đã được chia sẻ.");
        }

        return await _gateway.CreateUserPermissionAsync(
            conn, item.ExternalId!, email.Trim(), role, notify, ct);
    }

    public async Task<DrivePermissionDto> UpdatePermissionAsync(
        Guid userId,
        Guid itemId,
        string permissionId,
        DrivePermissionRole role,
        CancellationToken ct = default)
    {
        var (item, conn) = await ResolveDriveItemAsync(itemId, userId, ct);
        await EnsureNotOwnerAsync(conn, item.ExternalId!, permissionId, ct);
        return await _gateway.UpdatePermissionAsync(conn, item.ExternalId!, permissionId, role, ct);
    }

    public async Task RemovePermissionAsync(
        Guid userId,
        Guid itemId,
        string permissionId,
        CancellationToken ct = default)
    {
        var (item, conn) = await ResolveDriveItemAsync(itemId, userId, ct);
        await EnsureNotOwnerAsync(conn, item.ExternalId!, permissionId, ct);
        await _gateway.DeletePermissionAsync(conn, item.ExternalId!, permissionId, ct);
    }

    public async Task<DrivePermissionDto?> SetLinkSharingAsync(
        Guid userId,
        Guid itemId,
        bool enabled,
        DrivePermissionRole role = DrivePermissionRole.Reader,
        CancellationToken ct = default)
    {
        var (item, conn) = await ResolveDriveItemAsync(itemId, userId, ct);
        return await _gateway.SetLinkSharingAsync(conn, item.ExternalId!, enabled, role, ct);
    }

    /// <inheritdoc />
    public async Task<DriveLinkRestrictConflict?> DetectLinkRestrictConflictAsync(
        Guid userId,
        Guid itemId,
        CancellationToken ct = default)
    {
        var (item, conn) = await ResolveDriveItemAsync(itemId, userId, ct);

        // Folder gốc / không có parents → tắt link không đụng thư mục mẹ.
        var parentExternalId = TryGetFirstParentExternalId(item);
        if (string.IsNullOrEmpty(parentExternalId))
            return null;

        // Quyền hiện tại của file + folder mẹ (hỏi Google live, không cache DB).
        var itemPerms = await _gateway.ListPermissionsAsync(conn, item.ExternalId!, ct);
        var parentPerms = await _gateway.ListPermissionsAsync(conn, parentExternalId, ct);

        var itemHasLink = itemPerms.Any(p => p.IsLink || DrivePermissionTypes.IsLinkType(p.Type));
        var parentHasLink = parentPerms.Any(p => p.IsLink || DrivePermissionTypes.IsLinkType(p.Type));

        // Case 1 chỉ khi CẢ HAI đang "ai có link" — tắt file sẽ kéo theo folder mẹ (giống Drive).
        // Case 2 (folder private, file public): parentHasLink=false → null, không popup.
        if (!itemHasLink || !parentHasLink)
            return null;

        // Tên folder mẹ: ưu tiên Item local đã sync; fallback gọi Google GetFile.
        var parentLocal = await _items.GetByConnectionAndExternalIdAsync(
            userId, conn.Id, parentExternalId, ct);
        var parentTitle = parentLocal?.Title;
        if (string.IsNullOrWhiteSpace(parentTitle))
        {
            var parentFile = await _gateway.GetFileAsync(conn, parentExternalId, ct);
            parentTitle = string.IsNullOrWhiteSpace(parentFile.Name)
                ? parentExternalId
                : parentFile.Name!;
        }

        var itemTitle = string.IsNullOrWhiteSpace(item.Title) ? item.ExternalId! : item.Title;

        return new DriveLinkRestrictConflict(
            Code: DriveLinkRestrictConflict.RestrictAffectsParentCode,
            ItemId: item.Id,
            ItemTitle: itemTitle,
            ItemExternalId: item.ExternalId!,
            ParentItemId: parentLocal?.Id,
            ParentExternalId: parentExternalId,
            ParentTitle: parentTitle,
            ItemFromAccess: DriveLinkRestrictConflict.AccessAnyone,
            ItemToAccess: DriveLinkRestrictConflict.AccessRestricted,
            ParentFromAccess: DriveLinkRestrictConflict.AccessAnyone,
            ParentToAccess: DriveLinkRestrictConflict.AccessRestricted);
    }

    // ───────────────────────── Private helpers ─────────────────────────

    /// <summary>
    /// Resolve Item + Connection Drive hợp lệ cho mọi thao tác share.
    /// Map itemId (Guid app) → ExternalId (file id trên Google) trước khi gọi gateway.
    /// </summary>
    private async Task<(Item Item, Connection Connection)> ResolveDriveItemAsync(
        Guid itemId,
        Guid userId,
        CancellationToken ct)
    {
        var item = await _items.GetByIdAndUserAsync(itemId, userId, ct)
            ?? throw new NotFoundException("Item", itemId);

        if (item.Type != ItemType.File)
            throw new BusinessRuleException("Chỉ thao tác trên item loại File (Drive).");

        if (item.ConnectionId == null || string.IsNullOrEmpty(item.ExternalId))
            throw new BusinessRuleException("Item không liên kết Drive hợp lệ.");

        if (item.IsArchived)
            throw new BusinessRuleException("Không thể chia sẻ file đã trash.");

        var conn = await _connections.GetByIdAsync(item.ConnectionId.Value, ct);
        if (conn is null || conn.UserId != userId)
            throw new NotFoundException("Connection", item.ConnectionId.Value);

        if (conn.ServiceType != ServiceType.Drive)
            throw new BusinessRuleException("Connection không phải Google Drive.");

        if (conn.Status != ConnectionStatus.Active)
            throw new BusinessRuleException("Drive connection không active.");

        return (item, conn);
    }

    /// <summary>Validate connection Drive thuộc user — dùng cho CreateFolderAsync (chưa có item).</summary>
    private async Task<Connection> GetValidDriveConnectionAsync(
        Guid connectionId,
        Guid userId,
        CancellationToken ct)
    {
        var conn = await _connections.GetByIdAsync(connectionId, ct);
        if (conn is null || conn.UserId != userId)
            throw new NotFoundException("Connection", connectionId);

        if (conn.ServiceType != ServiceType.Drive)
            throw new BusinessRuleException("Connection không phải Google Drive.");

        if (conn.Status != ConnectionStatus.Active)
            throw new BusinessRuleException("Drive connection không active.");

        return conn;
    }

    /// <summary>Chặn sửa/xoá permission owner — UI chỉ hiển thị, không thao tác.</summary>
    private async Task EnsureNotOwnerAsync(
        Connection conn,
        string fileId,
        string permissionId,
        CancellationToken ct)
    {
        var perms = await _gateway.ListPermissionsAsync(conn, fileId, ct);
        var target = perms.FirstOrDefault(p => p.Id == permissionId)
            ?? throw new NotFoundException("Permission", permissionId);

        if (target.IsOwner)
            throw new BusinessRuleException("Không thể sửa/xoá quyền owner.");
    }

    private static void ValidateFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new BusinessRuleException("Tên folder không được để trống.");

        if (name.Trim().Length > 255)
            throw new BusinessRuleException("Tên folder tối đa 255 ký tự.");
    }

    /// <summary>
    /// Lấy Google folder id cha đầu tiên từ metadata.parents (sync A6 / upload đã ghi).
    /// Null = file ở gốc My Drive hoặc metadata chưa có parents.
    /// </summary>
    private static string? TryGetFirstParentExternalId(Item item)
    {
        if (string.IsNullOrEmpty(item.MetadataJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(item.MetadataJson);
            if (!doc.RootElement.TryGetProperty("parents", out var parents)
                || parents.ValueKind != JsonValueKind.Array
                || parents.GetArrayLength() == 0)
            {
                return null;
            }

            var first = parents[0].GetString();
            return string.IsNullOrWhiteSpace(first) ? null : first;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Kiểm tra item là folder Drive qua metadata (isFolder hoặc mimeType).
    /// A6 sẽ chuẩn hoá isFolder khi sync.
    /// </summary>
    private static bool IsDriveFolder(Item item)
    {
        if (string.IsNullOrEmpty(item.MetadataJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(item.MetadataJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("isFolder", out var isFolder) && isFolder.ValueKind == JsonValueKind.True)
                return true;

            if (root.TryGetProperty("mimeType", out var mime))
                return DriveMimeTypes.IsFolder(mime.GetString());
        }
        catch (JsonException)
        {
            // Metadata hỏng → coi như không phải folder.
        }

        return false;
    }

    private static ItemResponse MapToItemResponse(Item item) => new(
        Id: item.Id,
        Type: item.Type,
        Title: item.Title,
        Snippet: item.Snippet,
        Status: item.Status,
        OccurredAt: item.OccurredAt,
        DueAt: item.DueAt,
        IsImportant: item.IsImportant,
        ExternalId: item.ExternalId,
        MetadataJson: item.MetadataJson,
        FolderIds: new List<Guid>(),
        Tags: new List<ItemTag>(),
        ConnectionId: item.ConnectionId);
}
