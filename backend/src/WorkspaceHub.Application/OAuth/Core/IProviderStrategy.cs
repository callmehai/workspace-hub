namespace WorkspaceHub.Application.OAuth.Core;

/// <summary>
/// Strategy cho từng OAuth provider (Google, Jira, ...).
/// Mỗi provider implement 1 class riêng — ConnectionsService dispatch theo integrationKey.
/// </summary>
public interface IProviderStrategy
{
    /// <summary>Key khớp với Integration.Key trong DB ("google", "jira", ...).</summary>
    string ProviderKey { get; }

    /// <summary>Build authorization URL + cache CSRF state, trả về kết quả để controller redirect.</summary>
    Task<InitiateConnectionResult> BuildAuthUrlAsync(
        BuildAuthUrlRequest request,
        CancellationToken ct = default);

    Task<TokenExchangeResult> ExchangeCodeAsync(
        ExchangeCodeRequest request,
        CancellationToken ct = default);
}
