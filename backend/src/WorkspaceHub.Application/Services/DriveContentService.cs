using System.Text.Json;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

/// <summary>
/// Đọc nội dung file Drive (download + thumbnail). Resolve itemId (Guid app) → ExternalId (Google file id)
/// + Connection hợp lệ, rồi ủy thác Google cho <see cref="IDriveGateway"/>.
/// </summary>
public class DriveContentService : IDriveContentService
{
    private readonly IDriveGateway _gateway;
    private readonly IItemRepository _items;
    private readonly IConnectionRepository _connections;

    public DriveContentService(
        IDriveGateway gateway,
        IItemRepository items,
        IConnectionRepository connections)
    {
        _gateway = gateway;
        _items = items;
        _connections = connections;
    }

    public async Task<DriveMediaResult> DownloadAsync(Guid userId, Guid itemId, CancellationToken ct = default)
    {
        var (item, conn) = await ResolveDriveItemAsync(itemId, userId, ct);
        var mimeType = TryGetMimeType(item) ?? "application/octet-stream";

        if (DriveMimeTypes.IsFolder(mimeType))
            throw new BusinessRuleException("Không thể tải xuống một thư mục.");

        return await _gateway.DownloadFileAsync(conn, item.ExternalId!, mimeType, item.Title, ct);
    }

    public async Task<DriveMediaResult?> GetThumbnailAsync(Guid userId, Guid itemId, CancellationToken ct = default)
    {
        var (item, conn) = await ResolveDriveItemAsync(itemId, userId, ct);
        var mimeType = TryGetMimeType(item);

        // Folder không có thumbnail — khỏi gọi Google.
        if (mimeType != null && DriveMimeTypes.IsFolder(mimeType))
            return null;

        return await _gateway.GetThumbnailAsync(conn, item.ExternalId!, ct);
    }

    /// <summary>
    /// Resolve Item File + Connection Drive hợp lệ cho thao tác READ (download/thumbnail).
    /// Cho phép cả item đã trash (IsArchived) để user còn tải file trước khi mất — khác luồng ghi/share.
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

        var conn = await _connections.GetByIdAsync(item.ConnectionId.Value, ct);
        if (conn is null || conn.UserId != userId)
            throw new NotFoundException("Connection", item.ConnectionId.Value);

        if (conn.ServiceType != ServiceType.Drive)
            throw new BusinessRuleException("Connection không phải Google Drive.");

        if (conn.Status != ConnectionStatus.Active)
            throw new BusinessRuleException("Drive connection không active.");

        return (item, conn);
    }

    /// <summary>Đọc mimeType từ metadata.mimeType (sync/upload đã ghi). Null nếu thiếu/hỏng.</summary>
    private static string? TryGetMimeType(Item item)
    {
        if (string.IsNullOrEmpty(item.MetadataJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(item.MetadataJson);
            if (doc.RootElement.TryGetProperty("mimeType", out var mime) && mime.ValueKind == JsonValueKind.String)
            {
                var value = mime.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        catch (JsonException)
        {
            // Metadata hỏng → coi như không biết mime.
        }

        return null;
    }
}
