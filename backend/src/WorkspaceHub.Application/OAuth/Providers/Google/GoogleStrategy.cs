using System.Text.Json;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.OAuth.Core;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.OAuth.Providers.Google;

/// <summary>Strategy Google — build URL với service scopes (Gmail/GCal/Drive) + include_granted_scopes.</summary>
public class GoogleStrategy : IProviderStrategy
{
    private readonly IOAuthTokenClient _tokenClient;

    public GoogleStrategy(IOAuthTokenClient tokenClient)
    {
        _tokenClient = tokenClient;
    }

    public string ProviderKey => "google";

    public Task<InitiateConnectionResult> BuildAuthUrlAsync(
        BuildAuthUrlRequest request,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<ServiceType>(request.ServiceType, ignoreCase: true, out var serviceType)
            || !GoogleScopes.ServiceScopes.ContainsKey(serviceType))
            throw new BusinessRuleException($"Service '{request.ServiceType}' không phải Google service");

        var builder = new GoogleAuthUrlBuilder(request.ClientId, request.RedirectUri);
        var url = builder.BuildForService(string.Join(' ', GoogleScopes.BuildRequestScopes(serviceType)), request.State);

        return Task.FromResult(new InitiateConnectionResult(url, request.State));
    }

    public async Task<TokenExchangeResult> ExchangeCodeAsync(
        ExchangeCodeRequest request,
        CancellationToken ct = default)
    {
        var formData = new Dictionary<string, string>
        {
            ["code"] = request.Code,
            ["client_id"] = request.ClientId,
            ["client_secret"] = request.ClientSecret,
            ["redirect_uri"] = request.RedirectUri,
            ["grant_type"] = "authorization_code"
        };

        string json;
        try
        {
            json = await _tokenClient.PostFormAsync(request.Integration.TokenEndpoint, formData, ct);
        }
        catch (HttpRequestException)
        {
            throw new BusinessRuleException("Google từ chối code");
        }

        var googleToken = JsonSerializer.Deserialize<GoogleTokenResponse>(json)
            ?? throw new BusinessRuleException("Google từ chối code");
        // SCRUM-26: dev-placeholder fallback — xoá khi deploy production với Google account thật.
        var providerAccountId = googleToken.IdToken is not null
            ? IdTokenParser.ExtractProviderAccountId(googleToken.IdToken)
            : "dev-placeholder@gmail.com";

        if (!Enum.TryParse<ServiceType>(request.ServiceType, ignoreCase: true, out var requestedService))
            throw new BusinessRuleException($"Service '{request.ServiceType}' không hợp lệ");

        var grantedService = GoogleScopes.ValidateAndExtract(googleToken.Scope, requestedService);

        return new TokenExchangeResult(
            googleToken.AccessToken,
            googleToken.RefreshToken,
            googleToken.ExpiresIn,
            googleToken.Scope,
            providerAccountId,
            [grantedService]);
    }
}
