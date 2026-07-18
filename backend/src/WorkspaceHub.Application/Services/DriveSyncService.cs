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

    public async Task<SyncResult> SyncConnectionAsync(Connection connection, CancellationToken ct = default)
    {
        // 1. Lấy danh sách ID đã lưu để cập nhật
        var existingItems = await _items.GetTrackedByConnectionIdAsync(connection.Id, ct);
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
            var mapped = _mapper.ToItem(file, connection.UserId, connection.Id);

            if (existingItems.TryGetValue(file.Id, out var existing))
            {
                // Nếu file bị xoá (file.Trashed = true), ta cập nhật IsArchived = true
                if (file.Trashed)
                {
                    existing.IsArchived = true;
                }
                else if (existing.ETag != mapped.ETag)
                {
                    existing.Title = mapped.Title;
                    existing.Snippet = mapped.Snippet;
                    existing.MetadataJson = mapped.MetadataJson;
                    existing.ETag = mapped.ETag;
                    existing.OccurredAt = mapped.OccurredAt;
                    // KHÔNG đụng IsImportant: Drive không có khái niệm "quan trọng" nên mapper luôn
                    // trả false → re-sync sẽ xoá mất cờ user tự đánh dấu. Field thuần local.
                    // Keep Status intact to avoid resetting Kanban columns.
                }
                skipped++;
                continue;
            }

            // Nếu đây là file bị xoá/vào thùng rác mà DB ta chưa từng lưu -> Đừng lưu làm gì cả
            if (file.Trashed)
            {
                skipped++;
                continue;
            }

            newItems.Add(mapped);
            existingItems[file.Id] = mapped;
            created++;
        }

        if (newItems.Count > 0)
            await _items.AddRangeAsync(newItems, ct);

        // 5. Cập nhật cờ isTopLevel cho tất cả các item Drive
        // Một item là TopLevel nếu KHÔNG có parent nào của nó tồn tại trong danh sách existingItems.
        var allExternalIds = new HashSet<string>(existingItems.Keys);
        foreach (var item in existingItems.Values)
        {
            if (item.IsArchived || string.IsNullOrEmpty(item.MetadataJson)) continue;

            bool currentIsTopLevel = item.MetadataJson.Contains("\"isTopLevel\":true");
            bool isTopLevel = true;

            // Fast path extraction without full JSON deserialization
            int parentsIdx = item.MetadataJson.IndexOf("\"parents\":[");
            if (parentsIdx != -1)
            {
                int endIdx = item.MetadataJson.IndexOf("]", parentsIdx);
                if (endIdx != -1)
                {
                    string parentsStr = item.MetadataJson.Substring(parentsIdx + 11, endIdx - parentsIdx - 11);
                    var parts = parentsStr.Split('"');
                    for (int i = 1; i < parts.Length; i += 2)
                    {
                        if (allExternalIds.Contains(parts[i]))
                        {
                            isTopLevel = false;
                            break;
                        }
                    }
                }
            }

            if (isTopLevel != currentIsTopLevel)
            {
                try
                {
                    var metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(item.MetadataJson);
                    if (metadata != null)
                    {
                        metadata["isTopLevel"] = System.Text.Json.JsonSerializer.SerializeToElement(isTopLevel);
                        item.MetadataJson = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                    }
                }
                catch { /* ignore invalid json */ }
            }
        }

        connection.CursorType = CursorType.PageToken;
        connection.CursorValue = result.NextSyncCursor;
        connection.LastSyncedAt = DateTime.UtcNow;
        connection.Status = ConnectionStatus.Active;
        connection.LastError = null;

        _connections.Update(connection);
        await _connections.SaveChangesAsync(ct);

        return new SyncResult(scanned, created, skipped, result.NextSyncCursor);
    }
}