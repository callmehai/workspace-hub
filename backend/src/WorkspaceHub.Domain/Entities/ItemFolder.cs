namespace WorkspaceHub.Domain.Entities;

/// <summary>Junction Item ↔ Folder (m-n). Composite PK (ItemId, FolderId).</summary>
public class ItemFolder
{
    public Guid ItemId { get; set; }
    public Guid FolderId { get; set; }
    public int Position { get; set; }   // thứ tự Kanban trong cùng folder + status
    public DateTime AddedAt { get; set; }

    // Navigation
    public Item Item { get; set; } = null!;
    public Folder Folder { get; set; } = null!;
}
