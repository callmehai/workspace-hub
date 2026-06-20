namespace WorkspaceHub.Application.DTOs.Connections;

/// <summary>
/// DTO trả về thông tin connection của user. Token luôn masked (bảo mật).
/// Mô hình B: mỗi row = 1 service.
/// </summary>
public class ConnectionDto
{
    public Guid Id { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string ServiceType { get; init; } = string.Empty;
    public string ProviderAccountId { get; init; } = string.Empty;
    public string MaskedToken { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public DateTime? LastSyncedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}
