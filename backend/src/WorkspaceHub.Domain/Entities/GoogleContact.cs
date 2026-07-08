using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Domain.Entities;

/// <summary>Cache contact Google theo Connection Gmail — sync read-only, gợi ý To/Cc/Bcc (SCRUM-69).</summary>
public class GoogleContact
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string Email { get; set; } = null!;
    public string? DisplayName { get; set; }
    public GoogleContactSource Source { get; set; }
    public string? ExternalResourceName { get; set; }
    public DateTime SyncedAt { get; set; }

    public Connection Connection { get; set; } = null!;
}
