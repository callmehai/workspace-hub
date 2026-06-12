using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.OAuth.Core;

namespace WorkspaceHub.Application.OAuth.Providers.Jira;

/// <summary>
/// Strategy Jira (Atlassian OAuth 2.0 3LO).
/// Atlassian yêu cầu thêm param "audience" và scope string khác Google.
/// Docs: https://developer.atlassian.com/cloud/jira/platform/oauth-2-3lo-apps/
/// </summary>
public class JiraStrategy(
    Interfaces.Services.IOAuthTokenClient tokenClient,
    IHttpClientFactory httpClientFactory) : IProviderStrategy
{
    private const string AtlassianAudience = "api.atlassian.com";
    private const string MeEndpoint = "https://api.atlassian.com/me";


    public string ProviderKey => "jira";

    public Task<InitiateConnectionResult> BuildAuthUrlAsync(
        ProviderStrategyContext ctx,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<Domain.Enums.ServiceType>(ctx.ServiceType, ignoreCase: true, out var st) || st != Domain.Enums.ServiceType.Jira)
            throw new BusinessRuleException($"JiraStrategy chỉ hỗ trợ ServiceType 'Jira', nhận được '{ctx.ServiceType}'");

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"]     = ctx.ClientId;
        query["redirect_uri"]  = ctx.RedirectUri;
        query["response_type"] = "code";
        // TODO SCRUM-42: chốt scope chính thức khi làm OAuth Atlassian.
        query["scope"]         = string.Join(' ', JiraScopes.All);
        query["state"]         = ctx.State;
        query["audience"]      = AtlassianAudience;
        query["prompt"]        = "consent";

        var url = $"{ctx.Integration.AuthorizationEndpoint}?{query}";

        return Task.FromResult(new InitiateConnectionResult(url, ctx.State));
    }

    public async Task<TokenExchangeResult> ExchangeCodeAsync(
        CompleteContext ctx,
        CancellationToken ct = default)
    {
        // Step 1: exchange code → access_token + refresh_token
        var formData = new Dictionary<string, string>
        {
            ["grant_type"]    = "authorization_code",
            ["client_id"]     = ctx.ClientId,
            ["client_secret"] = ctx.ClientSecret,
            ["code"]          = ctx.Code,
            ["redirect_uri"]  = ctx.RedirectUri
        };

        string json;
        try
        {
            json = await tokenClient.PostFormAsync(ctx.Integration.TokenEndpoint, formData, ct);
        }
        catch (HttpRequestException)
        {
            throw new BusinessRuleException("Atlassian từ chối code");
        }

        var token = JsonSerializer.Deserialize<JiraTokenResponse>(json)
            ?? throw new BusinessRuleException("Atlassian từ chối code");

        // Step 2: GET /me → account_id (không có id_token như Google)
        var http = httpClientFactory.CreateClient("OAuthToken");
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);

        JsonElement me;
        try
        {
            me = await http.GetFromJsonAsync<JsonElement>(MeEndpoint, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            throw new BusinessRuleException("Không lấy được thông tin tài khoản từ Atlassian");
        }

        if (!me.TryGetProperty("account_id", out var accountIdEl))
            throw new BusinessRuleException("Không lấy được accountId từ Atlassian");

        var accountId = accountIdEl.GetString()
            ?? throw new BusinessRuleException("Không lấy được accountId từ Atlassian");

        // Step 3: validate scopes — Jira all-or-nothing, không phụ thuộc ServiceType được request.
        var grantedServices = JiraScopes.ValidateAndExtract(token.Scope);

        return new TokenExchangeResult(
            token.AccessToken,
            token.RefreshToken,
            token.ExpiresIn,
            token.Scope,
            accountId,
            grantedServices);
    }
}
