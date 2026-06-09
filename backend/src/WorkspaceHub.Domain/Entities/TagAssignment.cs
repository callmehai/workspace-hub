namespace WorkspaceHub.Domain.Entities;

/// <summary>Junction Tag ↔ Item (m-n). Composite PK (TagId, ItemId).</summary>
public class TagAssignment
{
    public Guid TagId { get; set; }
    public Guid ItemId { get; set; }
    public DateTime AssignedAt { get; set; }

    // Navigation
    public Tag Tag { get; set; } = null!;
    public Item Item { get; set; } = null!;
}
