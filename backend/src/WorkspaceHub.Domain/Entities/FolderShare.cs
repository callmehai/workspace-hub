using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Domain.Entities;

/// <summary>Owner mời bạn bè xem/chỉnh folder. AcceptedAt/DeclinedAt null → Pending.</summary>
public class FolderShare
{
    public Guid Id { get; set; }
    public Guid FolderId { get; set; }
    public Guid SharedWithUserId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public SharePermission Permission { get; set; } = SharePermission.Viewer;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;   // khi tạo share / gửi invite
    public DateTime? AcceptedAt { get; set; }   // not null = đã chấp nhận
    public DateTime? DeclinedAt { get; set; }    // not null = đã từ chối (vẫn giữ row để owner mời lại)
    public DateTime? ExpiresAt { get; set; }

    // Navigation
    public Folder Folder { get; set; } = null!;
    public User SharedWithUser { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}
