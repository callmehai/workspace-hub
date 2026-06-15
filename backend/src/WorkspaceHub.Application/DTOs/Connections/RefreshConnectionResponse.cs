namespace WorkspaceHub.Application.DTOs.Connections;

/// <summary>
/// Response trả về sau khi refresh token thành công. Chỉ trả expiresAt mới.
/// </summary>
public class RefreshConnectionResponse
{
    public Guid ConnectionId { get; init; }
    public DateTime ExpiresAt { get; init; }
    public string Status { get; init; } = string.Empty;
}
