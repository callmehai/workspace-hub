using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.OAuth.Core;

/// <summary>Kết quả build auth URL — URL redirect + state CSRF. Dùng chung cho mọi provider.</summary>
public record InitiateConnectionResult(string AuthorizationUrl, string State);

/// <summary>
/// Kết quả sau khi hoàn tất OAuth callback — trả về controller để map thành HTTP response.
/// Mô hình B: 1 lần grant có thể tạo/refresh nhiều Connection (1 row mỗi service được cấp).
/// </summary>
public record CompleteConnectionResult(
    string IntegrationKey,
    string ProviderAccountId,
    IReadOnlyList<ConnectionResult> Connections);

public record ConnectionResult(Guid Id, string ServiceType, string Status);

/// <summary>Dữ liệu đầu vào cho ExchangeCodeAsync — truyền từ ConnectionsService xuống strategy.</summary>
public record CompleteContext(
    string Code,
    string ClientId,
    string ClientSecret,
    string RedirectUri,
    Domain.Entities.Integration Integration);

/// <summary>
/// Kết quả token exchange — trả về cho ConnectionsService để build Connection (mô hình B).
/// GrantedServices: danh sách ServiceType được cấp phép — đã resolve từ raw scope string bởi strategy.
/// </summary>
public record TokenExchangeResult(
    string AccessToken,
    string? RefreshToken,
    int ExpiresIn,
    string RawScopes,
    string ProviderAccountId,
    IReadOnlyList<ServiceType> GrantedServices);

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

    /// <summary>
    /// Exchange authorization code → access/refresh token.
    /// Extract ProviderAccountId + resolve GrantedServices — tất cả logic provider-specific ở đây.
    /// </summary>
    Task<TokenExchangeResult> ExchangeCodeAsync(
        CompleteContext context,
        CancellationToken ct = default);
}

/// <summary>Dữ liệu đầu vào chung cho mọi strategy — truyền từ ConnectionsService xuống.</summary>
public record ProviderStrategyContext(
    string ClientId,
    string RedirectUri,
    string State,
    Domain.Entities.Integration Integration);
