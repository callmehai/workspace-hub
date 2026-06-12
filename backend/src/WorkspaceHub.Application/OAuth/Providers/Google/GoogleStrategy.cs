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
        ProviderStrategyContext ctx,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<ServiceType>(ctx.ServiceType, ignoreCase: true, out var serviceType)
            || !GoogleScopes.ServiceScopes.TryGetValue(serviceType, out var scopes))
            throw new BusinessRuleException($"Service '{ctx.ServiceType}' không phải Google service");

        var builder = new GoogleAuthUrlBuilder(ctx.ClientId, ctx.RedirectUri);
        var url = builder.BuildForService(string.Join(' ', scopes), ctx.State);

        return Task.FromResult(new InitiateConnectionResult(url, ctx.State));
    }

    public async Task<TokenExchangeResult> ExchangeCodeAsync(
        CompleteContext ctx,
        CancellationToken ct = default)
    {
        var formData = new Dictionary<string, string>
        {
            ["code"] = ctx.Code,
            ["client_id"] = ctx.ClientId,
            ["client_secret"] = ctx.ClientSecret,
            ["redirect_uri"] = ctx.RedirectUri,
            ["grant_type"] = "authorization_code"
        };

        string json;
        try
        {
            json = await _tokenClient.PostFormAsync(ctx.Integration.TokenEndpoint, formData, ct);
        }
        catch (HttpRequestException)
        {
            throw new BusinessRuleException("Google từ chối code");
        }

        var googleToken = JsonSerializer.Deserialize<GoogleTokenResponse>(json)
            ?? throw new BusinessRuleException("Google từ chối code");
        // TODO: bỏ fallback khi test thật với Google account.
        var providerAccountId = googleToken.IdToken is not null
            ? IdTokenParser.ExtractProviderAccountId(googleToken.IdToken)
            : "dev-placeholder@gmail.com";

        if (!Enum.TryParse<ServiceType>(ctx.ServiceType, ignoreCase: true, out var requestedService))
            throw new BusinessRuleException($"Service '{ctx.ServiceType}' không hợp lệ");

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
