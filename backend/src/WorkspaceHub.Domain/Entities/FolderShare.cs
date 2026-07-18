using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Domain.Entities;

/// <summary>Owner mời bạn bè xem/chỉnh folder. AcceptedAt=null → Pending (invite chưa accept).</summary>
public class FolderShare
{
    public Guid Id { get; set; }
    public Guid FolderId { get; set; }
    public Guid SharedWithUserId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public SharePermission Permission { get; set; } = SharePermission.Viewer;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;   // khi tạo share / gửi invite
    public DateTime? AcceptedAt { get; set; }   // null = pending; not null = accepted
    public DateTime? ExpiresAt { get; set; }

    // Navigation
    public Folder Folder { get; set; } = null!;
    public User SharedWithUser { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}
