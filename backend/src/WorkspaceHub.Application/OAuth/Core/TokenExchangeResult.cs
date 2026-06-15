using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.OAuth.Core;

/// <summary>
/// Kết quả token exchange — trả về cho ConnectionsService để build Connection (mô hình B).
/// GrantedServices: danh sách ServiceType được cấp phép — đã resolve từ raw scope string bởi strategy.
/// </summary>
public class TokenExchangeResult
{
    public string AccessToken { get; init; } = string.Empty;
    public string? RefreshToken { get; init; }
    public int ExpiresIn { get; init; }
    public string RawScopes { get; init; } = string.Empty;
    public string ProviderAccountId { get; init; } = string.Empty;
    public IReadOnlyList<ServiceType> GrantedServices { get; init; } = [];

    public TokenExchangeResult() { }

    public TokenExchangeResult(string accessToken, string? refreshToken, int expiresIn, string rawScopes, string providerAccountId, IReadOnlyList<ServiceType> grantedServices)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        ExpiresIn = expiresIn;
        RawScopes = rawScopes;
        ProviderAccountId = providerAccountId;
        GrantedServices = grantedServices;
    }
}
