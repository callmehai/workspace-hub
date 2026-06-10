using System.Web;

namespace WorkspaceHub.Application.OAuth;

/// <summary>
/// Strategy Jira (Atlassian OAuth 2.0 3LO).
/// Atlassian yêu cầu thêm param "audience" và scope string khác Google.
/// Docs: https://developer.atlassian.com/cloud/jira/platform/oauth-2-3lo-apps/
/// </summary>
public class JiraStrategy : IProviderStrategy
{
    private const string AtlassianAudience = "api.atlassian.com";

    public string ProviderKey => "jira";

    public Task<InitiateConnectionResult> BuildAuthUrlAsync(
        ProviderStrategyContext ctx,
        CancellationToken ct = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"]     = ctx.ClientId;
        query["redirect_uri"]  = ctx.RedirectUri;
        query["response_type"] = "code";
        query["scope"]         = ctx.Integration.DefaultScopes; // đọc từ DB seed
        query["state"]         = ctx.State;
        query["audience"]      = AtlassianAudience;             // bắt buộc với Atlassian
        query["prompt"]        = "consent";

        var url = $"{ctx.Integration.AuthorizationEndpoint}?{query}";

        return Task.FromResult(new InitiateConnectionResult(url, ctx.State));
    }
}
