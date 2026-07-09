using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Domain.Entities;

/// <summary>Lõi app — 1 đơn vị thông tin. Field riêng từng type lưu trong MetadataJson.</summary>
public class Item
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ItemType Type { get; set; }
    public string Title { get; set; } = null!;
    public string Snippet { get; set; } = null!;
    public string? ExternalId { get; set; }             // ID gốc provider; NULL cho Note
    public string? ThreadId { get; set; }                // Gmail threadId (Email) — gộp thread ở list; NULL cho loại khác
    public Guid? ConnectionId { get; set; }              // NULL cho Note; SET NULL khi xoá connection
    public string? ETag { get; set; }                    // version provider, so trước khi write-back (lệch → 409)
    public ItemStatus Status { get; set; } = ItemStatus.Inbox;
    public DateTime OccurredAt { get; set; }
    public DateTime? DueAt { get; set; }
    public bool IsImportant { get; set; }
    public bool IsArchived { get; set; }
    public string MetadataJson { get; set; } = "{}";     // field riêng từng type

    // Navigation
    public User User { get; set; } = null!;
    public Connection? Connection { get; set; }
    public ICollection<ItemFolder> ItemFolders { get; set; } = new List<ItemFolder>();
    public ICollection<TagAssignment> TagAssignments { get; set; } = new List<TagAssignment>();
}
