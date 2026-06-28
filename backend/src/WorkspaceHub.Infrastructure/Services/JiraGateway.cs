using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Infrastructure.Services;

/// <summary>
/// Gọi Jira Cloud REST API v3. Base URL theo cloudId của connection:
/// https://api.atlassian.com/ex/jira/{cloudId}/rest/api/3 (cloudId = Connection.ProviderAccountId — SCRUM-54).
/// Search dùng endpoint mới POST /search/jql (phân trang nextPageToken, không còn startAt).
/// </summary>
public class JiraGateway : IJiraGateway
{
    private readonly IAtlassianTokenService _tokenService;
    private readonly IHttpClientFactory _httpClientFactory;

    private const string ApiBaseFormat = "https://api.atlassian.com/ex/jira/{0}/rest/api/3";
    private const string DefaultJql = "(assignee = currentUser() OR reporter = currentUser()) ORDER BY updated ASC";

    private static readonly string[] RequestedFields =
        ["summary", "description", "status", "assignee", "priority", "issuetype", "project", "updated"];

    public JiraGateway(IAtlassianTokenService tokenService, IHttpClientFactory httpClientFactory)
    {
        _tokenService = tokenService;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<JiraSearchResult> SearchIssuesAsync(
        Connection connection,
        string? jql,
        string? pageToken,
        int maxResults,
        CancellationToken ct = default)
    {
        var http = await BuildClientAsync(connection, ct);
        var apiBase = string.Format(ApiBaseFormat, connection.ProviderAccountId);

        var body = new Dictionary<string, object?>
        {
            ["jql"]        = string.IsNullOrWhiteSpace(jql) ? DefaultJql : jql,
            ["maxResults"] = Math.Clamp(maxResults, 1, 100),
            ["fields"]     = RequestedFields
        };
        if (!string.IsNullOrEmpty(pageToken))
            body["nextPageToken"] = pageToken;

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync($"{apiBase}/search/jql", body, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException($"Jira API lỗi kết nối: {ex.Message}", ex);
        }

        await EnsureSuccessAsync(response, ct);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return ParseSearch(doc, connection.ProviderAccountId);
    }

    private async Task<HttpClient> BuildClientAsync(Connection connection, CancellationToken ct)
    {
        var accessToken = await _tokenService.GetFreshAccessTokenAsync(connection, ct);
        var http = _httpClientFactory.CreateClient("Jira");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    /// <summary>Map status code Jira về exception hệ thống: 403→Forbidden, 404→NotFound, còn lại→Provider(502).</summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var detail = await SafeReadBodyAsync(response, ct);

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new ForbiddenException("Token Jira hết hạn hoặc thiếu quyền — cần kết nối lại."),
            HttpStatusCode.Forbidden    => new ForbiddenException("Thiếu quyền truy cập Jira — cần kết nối lại với quyền đầy đủ."),
            HttpStatusCode.NotFound     => new NotFoundException("Jira resource", detail),
            _ => new ProviderException($"Jira API error {(int)response.StatusCode}: {detail}")
        };
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadAsStringAsync(ct); }
        catch { return response.ReasonPhrase ?? "unknown"; }
    }

    private static JiraSearchResult ParseSearch(JsonElement doc, string cloudId)
    {
        var issues = new List<JiraIssue>();

        if (doc.TryGetProperty("issues", out var issuesEl) && issuesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var issueEl in issuesEl.EnumerateArray())
                issues.Add(ParseIssue(issueEl, cloudId));
        }

        string? nextPageToken = doc.TryGetProperty("nextPageToken", out var tokEl) && tokEl.ValueKind == JsonValueKind.String
            ? tokEl.GetString()
            : null;

        bool isLast = doc.TryGetProperty("isLast", out var lastEl) && lastEl.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? lastEl.GetBoolean()
            : nextPageToken is null;

        return new JiraSearchResult(issues, nextPageToken, isLast);
    }

    private static JiraIssue ParseIssue(JsonElement issueEl, string cloudId)
    {
        var id = GetString(issueEl, "id") ?? string.Empty;
        var key = GetString(issueEl, "key") ?? string.Empty;

        JsonElement fields = issueEl.TryGetProperty("fields", out var f) ? f : default;

        string? summary = GetString(fields, "summary");
        JsonElement? description = fields.ValueKind == JsonValueKind.Object
                                   && fields.TryGetProperty("description", out var descEl)
                                   && descEl.ValueKind == JsonValueKind.Object
            ? descEl
            : null;

        string? statusName    = GetNestedString(fields, "status", "name");
        string? assignee      = GetNestedString(fields, "assignee", "displayName");
        string? priorityName  = GetNestedString(fields, "priority", "name");
        string? issueTypeName = GetNestedString(fields, "issuetype", "name");
        string? projectKey    = GetNestedString(fields, "project", "key");

        DateTimeOffset? updated = null;
        var updatedStr = GetString(fields, "updated");
        if (!string.IsNullOrEmpty(updatedStr) && DateTimeOffset.TryParse(updatedStr, out var parsed))
            updated = parsed;

        var issueUrl = $"https://api.atlassian.com/ex/jira/{cloudId}/browse/{key}";

        return new JiraIssue(
            id, key, projectKey, summary, description,
            statusName, assignee, priorityName, issueTypeName, issueUrl, updated);
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(prop, out var p)
        && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static string? GetNestedString(JsonElement el, string prop, string childProp) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(prop, out var p)
        && p.ValueKind == JsonValueKind.Object
            ? GetString(p, childProp)
            : null;
}
