using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

public class DriveSyncService : IDriveSyncService
{
    private readonly IGoogleDriveGateway _gateway;
    private readonly IDriveItemMapper _mapper;
    private readonly IItemRepository _items;
    private readonly IConnectionRepository _connections;

    public DriveSyncService(
        IGoogleDriveGateway gateway,
        IDriveItemMapper mapper,
        IItemRepository items,
        IConnectionRepository connections)
    {
        _gateway = gateway;
        _mapper = mapper;
        _items = items;
        _connections = connections;
    }

    private async Task<Connection> GetValidConnectionAsync(Guid connectionId, Guid userId, CancellationToken ct)
    {
        var conn = await _connections.GetByIdAsync(connectionId, ct);
        if (conn is null || conn.UserId != userId)
            throw new WorkspaceHub.Application.Common.NotFoundException("Connection", connectionId);

        if (conn.ServiceType != ServiceType.Drive)
            throw new WorkspaceHub.Application.Common.BusinessRuleException("Kết nối này không phải Google Drive");

        return conn;
    }

    public async Task<SyncResult> SyncAsync(Guid connectionId, Guid userId, CancellationToken ct = default)
    {
        var conn = await GetValidConnectionAsync(connectionId, userId, ct);
        return await SyncConnectionAsync(conn, ct);
    }

    public async Task<SyncResult> SyncConnectionAsync(Connection connection, CancellationToken ct = default)
    {
        // 1. Lấy danh sách ID đã lưu để tránh tạo trùng item
        var existing = await _items.GetExistingExternalIdsAsync(connection.Id, ct);
        var newItems = new List<Item>();

        // 2. Lấy thẻ đánh dấu trang của Google Drive (PageToken)
        string? pageToken = connection.CursorType == CursorType.PageToken ? connection.CursorValue : null;

        // 3. Yêu cầu Gateway gọi API lấy thay đổi mới
        var result = await _gateway.SyncFilesAsync(connection, pageToken, ct);

        // NẾU token bị mốc (hết hạn), Gateway báo Expired. Mình gọi lại lần 2 bằng null để Full Sync
        if (result.Expired)
        {
            result = await _gateway.SyncFilesAsync(connection, null, ct);
        }

        int scanned = result.Files.Count;
        int created = 0;
        int skipped = 0;

        // 4. Lọc trùng & Dịch sang định dạng Item (Mapping)
        foreach (var file in result.Files)
        {
            if (existing.Contains(file.Id))
            {
                // Ở phiên bản thực tế, đoạn này nếu file bị xoá (file.Trashed = true), 
                // ta nên gọi hàm Update cái Item cũ trong DB để IsArchived = true.
                // Tuy nhiên đây là bản MVP siêu tốc, tạm thời ta đếm vào số skipped để đơn giản.
                skipped++;
                continue;
            }

            // Nếu đây là file bị xoá/vào thùng rác mà DB ta chưa từng lưu -> Đừng lưu làm gì cả
            if (file.Trashed)
            {
                skipped++;
                continue;
            }

            // Dịch ra Entity Item
            var item = _mapper.ToItem(file, connection.UserId, connection.Id);
            newItems.Add(item);
            existing.Add(file.Id);
            created++;
        }

        if (newItems.Count > 0)
            await _items.AddRangeAsync(newItems, ct);

        connection.CursorType = CursorType.PageToken;
        connection.CursorValue = result.NextPageToken;
        connection.LastSyncedAt = DateTime.UtcNow;
        connection.Status = ConnectionStatus.Active;
        connection.LastError = null;

        _connections.Update(connection);
        await _connections.SaveChangesAsync(ct);

        return new SyncResult(scanned, created, skipped, result.NextPageToken);
    }
}