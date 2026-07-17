using System.Text.Json;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Mapping;

public class DriveItemMapper : IDriveItemMapper
{
    public Item ToItem(DriveFileDto file, Guid userId, Guid connectionId, bool? isTopLevel = null)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["mimeType"] = file.MimeType,
            ["isFolder"] = DriveMimeTypes.IsFolder(file.MimeType),
            ["size"] = file.Size,
            ["webViewLink"] = file.WebViewLink,
            ["iconLink"] = file.IconLink,
        };

        if (file.Parents is { Count: > 0 })
            metadata["parents"] = file.Parents;

        // Luồng tạo/upload truyền cờ để item hiện NGAY ở view root (repo lọc "isTopLevel":true).
        // Null = để trống, DriveSyncService sẽ tự tính sau (giữ hành vi sync cũ).
        if (isTopLevel.HasValue)
            metadata["isTopLevel"] = isTopLevel.Value;

        return new Item
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = ItemType.File,
            Title = string.IsNullOrEmpty(file.Name) ? "(Không có tên)" : file.Name,
            Snippet = string.Empty,
            ExternalId = file.Id,
            ConnectionId = connectionId,
            Status = ItemStatus.Inbox,
            OccurredAt = file.ModifiedTime?.UtcDateTime ?? DateTime.UtcNow,
            IsImportant = false,
            IsArchived = false,
            //Chuyển đổi metadata sang JSON và lưu vào MetadataJson
            MetadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }),
            ETag = file.Version?.ToString() ?? file.HeadRevisionId ?? file.ModifiedTime?.ToString("o")
        };
    }
}