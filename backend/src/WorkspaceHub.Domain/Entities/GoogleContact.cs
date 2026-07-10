using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Domain.Entities;

/// <summary>Cache contact Google theo Connection Gmail — sync + write-back CRUD (SCRUM-69/76).</summary>
public class GoogleContact
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    /// <summary>Email chính (denormalized từ MetadataJson) — null khi Google person không có email.</summary>
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public GoogleContactSource Source { get; set; }
    public string? ExternalResourceName { get; set; }
    public string? Etag { get; set; }
    public DateTime SyncedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    /// <summary>Profile mở rộng (givenName, phones, birthday, …) — JSON camelCase.</summary>
    public string? MetadataJson { get; set; }

    public Connection Connection { get; set; } = null!;
}
