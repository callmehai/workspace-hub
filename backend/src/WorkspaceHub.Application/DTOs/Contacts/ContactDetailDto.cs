using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.DTOs.Contacts;

public class ContactDetailDto
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string Email { get; set; } = null!;
    public string? DisplayName { get; set; }
    public GoogleContactSource Source { get; set; }
    public string? Etag { get; set; }
    public DateTime SyncedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public ContactProfileDto Profile { get; set; } = new();
    public bool ReadOnly { get; set; }
}
