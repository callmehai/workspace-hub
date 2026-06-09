namespace WorkspaceHub.Domain.Entities;

/// <summary>Container theo context. Mỗi folder có Kanban 3 cột.</summary>
public class Folder
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = null!;
    public string Color { get; set; } = null!;
    public string Icon { get; set; } = null!;
    public int SortOrder { get; set; }
    public bool IsArchived { get; set; }

    // Navigation
    public User Owner { get; set; } = null!;
    public ICollection<ItemFolder> ItemFolders { get; set; } = new List<ItemFolder>();
    public ICollection<FolderShare> FolderShares { get; set; } = new List<FolderShare>();
}
