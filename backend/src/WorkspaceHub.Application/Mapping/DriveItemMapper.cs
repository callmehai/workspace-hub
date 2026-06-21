using System.Text.Json;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Mapping;

public class DriveItemMapper : IDriveItemMapper
{
    public Item ToItem(DriveFileDto file, Guid userId, Guid connectionId)
    {
        var metadata = new
        {
            mimeType = file.MimeType,
            size = file.Size,
            webViewLink = file.WebViewLink,
            iconLink = file.IconLink
        };

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
            MetadataJson = JsonSerializer.Serialize(metadata),
            ETag = file.Version?.ToString() ?? file.HeadRevisionId ?? file.ModifiedTime?.ToString("o")
        };
    }
}