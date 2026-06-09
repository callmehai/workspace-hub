using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Domain.Entities;

/// <summary>1 grant OAuth thật của user vào 1 integration. Token cột Encrypted (encrypt ở ticket sau).</summary>
public class OAuthConnection
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid IntegrationId { get; set; }
    public string ProviderAccountId { get; set; } = null!;  // email/sub — phân biệt nhiều account
    public string AccessTokenEncrypted { get; set; } = null!;
    public string RefreshTokenEncrypted { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public string Scopes { get; set; } = null!;
    public ConnectionStatus Status { get; set; } = ConnectionStatus.Active;
    public DateTime? LastRefreshedAt { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public Integration Integration { get; set; } = null!;
    public ICollection<ServiceConnection> ServiceConnections { get; set; } = new List<ServiceConnection>();
}
