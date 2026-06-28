using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Abstractions;

/// <summary>
/// Cung cấp access token Atlassian còn hạn cho connection (refresh qua offline_access nếu cần).
/// Tách khỏi <see cref="ITokenService"/> vì Atlassian refresh khác Google (endpoint + payload riêng).
/// </summary>
public interface IAtlassianTokenService
{
    Task<string> GetFreshAccessTokenAsync(Connection connection, CancellationToken ct = default);
}
