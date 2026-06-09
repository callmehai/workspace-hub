using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Domain.Entities;

/// <summary>Owner mời teammate xem folder. MVP chỉ Viewer (read-only metadata).</summary>
public class FolderShare
{
    public Guid Id { get; set; }
    public Guid FolderId { get; set; }
    public Guid SharedWithUserId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public SharePermission Permission { get; set; } = SharePermission.Viewer;
    public DateTime? AcceptedAt { get; set; }   // null = pending
    public DateTime? ExpiresAt { get; set; }

    // Navigation
    public Folder Folder { get; set; } = null!;
    public User SharedWithUser { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}
