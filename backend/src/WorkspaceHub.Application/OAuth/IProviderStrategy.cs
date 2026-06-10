namespace WorkspaceHub.Application.OAuth;

/// <summary>Kết quả build auth URL — URL redirect + state CSRF. Dùng chung cho mọi provider.</summary>
public record InitiateConnectionResult(string AuthorizationUrl, string State);

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
        ProviderStrategyContext context,
        CancellationToken ct = default);
}

/// <summary>Dữ liệu đầu vào chung cho mọi strategy — truyền từ ConnectionsService xuống.</summary>
public record ProviderStrategyContext(
    string ClientId,
    string RedirectUri,
    string State,
    Domain.Entities.Integration Integration);
