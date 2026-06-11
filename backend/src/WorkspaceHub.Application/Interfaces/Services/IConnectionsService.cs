using WorkspaceHub.Application.OAuth.Core;

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
    /// Verify CSRF state, exchange code → token, persist Connections (mô hình B: 1 row mỗi service được cấp).
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
