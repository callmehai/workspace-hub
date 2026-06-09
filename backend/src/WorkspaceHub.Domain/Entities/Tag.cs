namespace WorkspaceHub.Domain.Entities;

/// <summary>Label user tự tạo, filter chéo. Private (không share). Name KHÔNG unique toàn hệ thống.</summary>
public class Tag
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = null!;
    public string Color { get; set; } = null!;

    // Navigation
    public User User { get; set; } = null!;
    public ICollection<TagAssignment> TagAssignments { get; set; } = new List<TagAssignment>();
}
