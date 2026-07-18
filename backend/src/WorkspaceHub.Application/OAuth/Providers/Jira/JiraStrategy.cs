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
    private const string AccessibleResourcesEndpoint = "https://api.atlassian.com/oauth/token/accessible-resources";


    public string ProviderKey => "atlassian";

    public Task<InitiateConnectionResult> BuildAuthUrlAsync(
        BuildAuthUrlRequest request,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<Domain.Enums.ServiceType>(request.ServiceType, ignoreCase: true, out var st) || st != Domain.Enums.ServiceType.Jira)
            throw new BusinessRuleException($"JiraStrategy chỉ hỗ trợ ServiceType 'Jira', nhận được '{request.ServiceType}'");

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"]     = request.ClientId;
        query["redirect_uri"]  = request.RedirectUri;
        query["response_type"] = "code";
        // TODO SCRUM-42: chốt scope chính thức khi làm OAuth Atlassian.
        query["scope"]         = string.Join(' ', JiraScopes.All);
        query["state"]         = request.State;
        query["audience"]      = AtlassianAudience;
        // Jira KHÔNG làm multi-connection (chốt scope) → giữ "consent", không thêm "select_account".
        query["prompt"]        = "consent";

        var url = $"{request.Integration.AuthorizationEndpoint}?{query}";

        return Task.FromResult(new InitiateConnectionResult(url, request.State));
    }

    public async Task<TokenExchangeResult> ExchangeCodeAsync(
        ExchangeCodeRequest request,
        CancellationToken ct = default)
    {
        // Step 1: exchange code → access_token + refresh_token
        var formData = new Dictionary<string, string>
        {
            ["grant_type"]    = "authorization_code",
            ["client_id"]     = request.ClientId,
            ["client_secret"] = request.ClientSecret,
            ["code"]          = request.Code,
            ["redirect_uri"]  = request.RedirectUri
        };

        string json;
        try
        {
            json = await tokenClient.PostFormAsync(request.Integration.TokenEndpoint, formData, ct);
        }
        catch (HttpRequestException)
        {
            throw new BusinessRuleException("Atlassian từ chối code");
        }

        var token = JsonSerializer.Deserialize<JiraTokenResponse>(json)
            ?? throw new BusinessRuleException("Atlassian từ chối code");

        // Step 2: GET /oauth/token/accessible-resources → cloudId của Jira site.
        // QUAN TRỌNG: ProviderAccountId phải là cloudId (KHÔNG phải account_id), vì base URL gọi Jira REST
        // là https://api.atlassian.com/ex/jira/{cloudId}/rest/api/3 (xem JiraGateway). Lưu account_id sẽ làm mọi call 404.
        var http = httpClientFactory.CreateClient("OAuthToken");
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);

        JsonElement resources;
        try
        {
            resources = await http.GetFromJsonAsync<JsonElement>(AccessibleResourcesEndpoint, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            throw new BusinessRuleException("Không lấy được danh sách Jira site từ Atlassian");
        }

        if (resources.ValueKind != JsonValueKind.Array || resources.GetArrayLength() == 0)
            throw new BusinessRuleException("Tài khoản Atlassian chưa có quyền truy cập Jira site nào");

        // Atlassian KHÔNG cho biết user chọn site nào ở màn consent (accessible-resources trả
        // CỘNG DỒN mọi site đã cấp quyền). Heuristic: chọn site đầu tiên CHƯA có connection Active
        // — connect lần 2 cùng account sẽ ăn site kế tiếp (multi-site từng-grant-một). Mỗi lần
        // connect = 1 grant riêng → refresh token độc lập, không dính vụ "chung token xoay vòng".
        // Tất cả site đều Active rồi → rơi về site đầu (ConnectionsService sẽ upsert = làm mới token).
        string? cloudId = null;
        foreach (var site in resources.EnumerateArray())
        {
            if (!site.TryGetProperty("id", out var idEl) || idEl.GetString() is not { Length: > 0 } id)
                continue;
            cloudId ??= id; // fallback: site hợp lệ đầu tiên
            if (!request.ExistingActiveProviderAccountIds.Contains(id))
            {
                cloudId = id;
                break;
            }
        }
        if (cloudId is null)
            throw new BusinessRuleException("Không lấy được cloudId từ Atlassian");

        // Step 3: validate scopes — Jira all-or-nothing, không phụ thuộc ServiceType được request.
        var grantedServices = JiraScopes.ValidateAndExtract(token.Scope);

        return new TokenExchangeResult(
            token.AccessToken,
            token.RefreshToken,
            token.ExpiresIn,
            token.Scope,
            cloudId,
            grantedServices);
    }
}
