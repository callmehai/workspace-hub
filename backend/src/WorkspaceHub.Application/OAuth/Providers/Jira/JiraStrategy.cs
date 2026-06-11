using System.Web;
using System.Net.Http.Json;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.OAuth.Core;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.OAuth.Providers.Jira;

/// <summary>
/// Strategy Jira (Atlassian OAuth 2.0 3LO).
/// Atlassian yêu cầu thêm param "audience" và scope string khác Google.
/// Docs: https://developer.atlassian.com/cloud/jira/platform/oauth-2-3lo-apps/
/// </summary>
public class JiraStrategy : IProviderStrategy
{
    private const string AtlassianAudience = "api.atlassian.com";

    // Scope suy từ code, không lưu DB (mô hình B đã bỏ cột DefaultScopes).
    // TODO SCRUM-42: chốt scope chính thức khi làm OAuth Atlassian.
    private const string JiraScopes = "read:jira-work write:jira-work offline_access";

    public string ProviderKey => "jira";

    public Task<InitiateConnectionResult> BuildAuthUrlAsync(
        ProviderStrategyContext ctx,
        CancellationToken ct = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"]     = ctx.ClientId;
        query["redirect_uri"]  = ctx.RedirectUri;
        query["response_type"] = "code";
        query["scope"]         = JiraScopes;
        query["state"]         = ctx.State;
        query["audience"]      = AtlassianAudience;             // bắt buộc với Atlassian
        query["prompt"]        = "consent";

        var url = $"{ctx.Integration.AuthorizationEndpoint}?{query}";

        return Task.FromResult(new InitiateConnectionResult(url, ctx.State));
    }

    public Task<TokenExchangeResult> ExchangeCodeAsync(
        CompleteContext ctx,
        CancellationToken ct = default)
    {
        // TODO: Jira token exchange (Atlassian OAuth 2.0 3LO).
        // 1. POST to ctx.Integration.TokenEndpoint with code + credentials.
        // 2. GET https://api.atlassian.com/me với access_token → ProviderAccountId = accountId field.
        // 3. Map Jira-specific scopes → IReadOnlyList<ServiceType>.
        // Docs: https://developer.atlassian.com/cloud/jira/platform/oauth-2-3lo-apps/
        throw new NotImplementedException("Jira ExchangeCodeAsync chưa được triển khai");
    }
}
