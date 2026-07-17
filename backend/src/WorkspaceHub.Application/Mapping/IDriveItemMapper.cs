using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Mapping;

public interface IDriveItemMapper
{
    /// <summary>
    /// Map metadata Drive → Item. <paramref name="isTopLevel"/>: set cờ hiển thị ở view root
    /// (true = item nằm ngay My Drive root). Null (mặc định) = KHÔNG ghi cờ — dành cho sync,
    /// nơi <c>DriveSyncService</c> tự tính isTopLevel sau khi có đủ tập item. Luồng tạo/upload
    /// PHẢI truyền rõ (parentExternalId == null) để item hiện ngay, không phải chờ sync.
    /// </summary>
    Item ToItem(DriveFileDto file, Guid userId, Guid connectionId, bool? isTopLevel = null);
}
