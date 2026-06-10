using WorkspaceHub.Application.OAuth;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>Nghiệp vụ OAuth connection: build authorization URL, exchange code, disconnect.</summary>
public interface IConnectionsService
{
    /// <summary>
    /// Tra Integration, decrypt ClientId, dispatch sang đúng IProviderStrategy, cache CSRF state.
    /// </summary>
    Task<InitiateConnectionResult> InitiateConnectionAsync(
        string integrationKey,
        string redirectUri,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Verify CSRF state, exchange code → token, persist OAuthConnection + ServiceConnections.
    /// </summary>
    Task<CompleteConnectionResult> CompleteConnectionAsync(
        string code,
        string state,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Encrypt clientId + clientSecret rồi lưu vào Integration.
    /// TODO: giới hạn [Authorize(Policy="AdminOnly")] sau khi JWT xong.
    /// </summary>
    Task SetCredentialsAsync(
        string integrationKey,
        string clientId,
        string clientSecret,
        CancellationToken ct = default);
}
