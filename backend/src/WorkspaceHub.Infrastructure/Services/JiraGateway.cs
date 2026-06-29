using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Mapping;
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
        return ParseSearch(doc);
    }

    public async Task<JiraCreatedIssue> CreateIssueAsync(
        Connection connection,
        CreateJiraIssueRequest request,
        CancellationToken ct = default)
    {
        var http = await BuildClientAsync(connection, ct);
        var apiBase = string.Format(ApiBaseFormat, connection.ProviderAccountId);

        var fields = new Dictionary<string, object?>
        {
            ["project"]   = new { key = request.ProjectKey },
            ["issuetype"] = new { name = request.IssueType },
            ["summary"]   = request.Summary
        };

        var adf = AdfConverter.FromPlainText(request.Description);
        if (adf is not null)
            fields["description"] = adf;

        if (!string.IsNullOrWhiteSpace(request.AssigneeAccountId))
            fields["assignee"] = new { accountId = request.AssigneeAccountId };

        if (!string.IsNullOrWhiteSpace(request.PriorityName))
            fields["priority"] = new { name = request.PriorityName };

        if (request.Labels is { Count: > 0 })
            fields["labels"] = request.Labels;

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync($"{apiBase}/issue", new { fields }, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException($"Jira API lỗi kết nối: {ex.Message}", ex);
        }

        // 400 từ Jira khi tạo = field/project/issueType không hợp lệ → BusinessRule (422) cho client.
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var detail = await SafeReadBodyAsync(response, ct);
            throw new BusinessRuleException($"Jira từ chối tạo issue (field không hợp lệ): {detail}");
        }

        await EnsureSuccessAsync(response, ct);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var id = GetString(doc, "id") ?? string.Empty;
        var key = GetString(doc, "key") ?? string.Empty;
        return new JiraCreatedIssue(id, key);
    }

    public async Task<JiraIssue> GetIssueAsync(Connection connection, string issueIdOrKey, CancellationToken ct = default)
    {
        var http = await BuildClientAsync(connection, ct);
        var apiBase = string.Format(ApiBaseFormat, connection.ProviderAccountId);
        var fieldsQuery = string.Join(",", RequestedFields);

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync($"{apiBase}/issue/{Uri.EscapeDataString(issueIdOrKey)}?fields={fieldsQuery}", ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException($"Jira API lỗi kết nối: {ex.Message}", ex);
        }

        await EnsureSuccessAsync(response, ct);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return ParseIssue(doc);
    }

    // ───────────────────── Write-back (SCRUM-57) ─────────────────────

    public async Task UpdateIssueAsync(Connection connection, string issueIdOrKey, UpdateJiraIssueRequest request, CancellationToken ct = default)
    {
        var fields = new Dictionary<string, object?>();

        if (request.Summary != null)
            fields["summary"] = request.Summary;

        if (request.Description != null)
        {
            // Description rỗng → ADF doc RỖNG (content:[]) để xoá nội dung; có text → ADF.
            fields["description"] = AdfConverter.FromPlainTextOrEmptyDoc(request.Description);
        }

        if (request.PriorityName != null)
            fields["priority"] = new { name = request.PriorityName };

        if (request.Labels != null)
            fields["labels"] = request.Labels;

        if (fields.Count == 0)
            return;

        var url = $"{ApiBase(connection)}/issue/{Uri.EscapeDataString(issueIdOrKey)}";
        await SendWriteAsync(connection, HttpMethod.Put, url, new { fields }, ct);
    }

    public async Task AssignIssueAsync(Connection connection, string issueIdOrKey, string? accountId, CancellationToken ct = default)
    {
        var url = $"{ApiBase(connection)}/issue/{Uri.EscapeDataString(issueIdOrKey)}/assignee";
        // accountId null/"-1" → unassign (Jira nhận accountId=null để bỏ assignee).
        var body = string.IsNullOrWhiteSpace(accountId) || accountId == "-1"
            ? (object)new { accountId = (string?)null }
            : new { accountId };
        await SendWriteAsync(connection, HttpMethod.Put, url, body, ct);
    }

    public async Task<IReadOnlyList<JiraTransition>> GetTransitionsAsync(Connection connection, string issueIdOrKey, CancellationToken ct = default)
    {
        var http = await BuildClientAsync(connection, ct);
        var url = $"{ApiBase(connection)}/issue/{Uri.EscapeDataString(issueIdOrKey)}/transitions";

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException($"Jira API lỗi kết nối: {ex.Message}", ex);
        }

        await EnsureSuccessAsync(response, ct);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var list = new List<JiraTransition>();
        if (doc.TryGetProperty("transitions", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in arr.EnumerateArray())
            {
                var id = GetString(t, "id");
                var name = GetString(t, "name");
                if (id is null || name is null) continue;
                var toStatus = t.TryGetProperty("to", out var to) && to.ValueKind == JsonValueKind.Object
                    ? GetString(to, "name")
                    : null;
                list.Add(new JiraTransition(id, name, toStatus));
            }
        }
        return list;
    }

    public async Task TransitionIssueAsync(Connection connection, string issueIdOrKey, string transitionId, CancellationToken ct = default)
    {
        var url = $"{ApiBase(connection)}/issue/{Uri.EscapeDataString(issueIdOrKey)}/transitions";
        await SendWriteAsync(connection, HttpMethod.Post, url, new { transition = new { id = transitionId } }, ct);
    }

    public async Task AddCommentAsync(Connection connection, string issueIdOrKey, string commentBody, CancellationToken ct = default)
    {
        var url = $"{ApiBase(connection)}/issue/{Uri.EscapeDataString(issueIdOrKey)}/comment";
        var body = new { body = AdfConverter.FromPlainText(commentBody) };
        await SendWriteAsync(connection, HttpMethod.Post, url, body, ct);
    }

    public async Task DeleteIssueAsync(Connection connection, string issueIdOrKey, CancellationToken ct = default)
    {
        var http = await BuildClientAsync(connection, ct);
        var url = $"{ApiBase(connection)}/issue/{Uri.EscapeDataString(issueIdOrKey)}?deleteSubtasks=true";

        HttpResponseMessage response;
        try
        {
            response = await http.DeleteAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException($"Jira API lỗi kết nối: {ex.Message}", ex);
        }

        // EnsureSuccess map 403→Forbidden, 404→NotFound, còn lại→Provider(502). KHÔNG nuốt lỗi (AC SCRUM-58).
        await EnsureSuccessAsync(response, ct);
    }

    // ───────────────────── Metadata helpers (SCRUM-59) ─────────────────────

    public async Task<IReadOnlyList<JiraProject>> GetProjectsAsync(Connection connection, CancellationToken ct = default)
    {
        // /project/search phân trang; lấy tối đa 100 project đầu (đủ cho dropdown đồ án).
        var doc = await GetJsonAsync(connection, $"{ApiBase(connection)}/project/search?maxResults=100", ct);

        var list = new List<JiraProject>();
        if (doc.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in values.EnumerateArray())
            {
                var id = GetString(p, "id");
                var key = GetString(p, "key");
                var name = GetString(p, "name");
                if (id is null || key is null) continue;
                list.Add(new JiraProject(id, key, name ?? key));
            }
        }
        return list;
    }

    public async Task<IReadOnlyList<JiraIssueType>> GetIssueTypesAsync(Connection connection, string projectKey, CancellationToken ct = default)
    {
        // GET /project/{key} trả về object có "issueTypes"[].
        var doc = await GetJsonAsync(connection, $"{ApiBase(connection)}/project/{Uri.EscapeDataString(projectKey)}", ct);

        var list = new List<JiraIssueType>();
        if (doc.TryGetProperty("issueTypes", out var types) && types.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in types.EnumerateArray())
            {
                var id = GetString(t, "id");
                var name = GetString(t, "name");
                if (id is null || name is null) continue;
                bool subtask = t.TryGetProperty("subtask", out var st) && st.ValueKind == JsonValueKind.True;
                list.Add(new JiraIssueType(id, name, subtask));
            }
        }
        return list;
    }

    public async Task<IReadOnlyList<JiraPriority>> GetPrioritiesAsync(Connection connection, CancellationToken ct = default)
    {
        // GET /priority trả về mảng phẳng.
        var doc = await GetJsonAsync(connection, $"{ApiBase(connection)}/priority", ct);

        var list = new List<JiraPriority>();
        if (doc.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in doc.EnumerateArray())
            {
                var id = GetString(p, "id");
                var name = GetString(p, "name");
                if (id is null || name is null) continue;
                list.Add(new JiraPriority(id, name));
            }
        }
        return list;
    }

    public async Task<IReadOnlyList<JiraUser>> GetAssignableUsersAsync(Connection connection, string projectKey, string? query, CancellationToken ct = default)
    {
        var url = $"{ApiBase(connection)}/user/assignable/search?project={Uri.EscapeDataString(projectKey)}&maxResults=50";
        if (!string.IsNullOrWhiteSpace(query))
            url += $"&query={Uri.EscapeDataString(query)}";

        var doc = await GetJsonAsync(connection, url, ct);

        var list = new List<JiraUser>();
        if (doc.ValueKind == JsonValueKind.Array)
        {
            foreach (var u in doc.EnumerateArray())
            {
                var accountId = GetString(u, "accountId");
                if (accountId is null) continue;
                var displayName = GetString(u, "displayName") ?? accountId;
                var email = GetString(u, "emailAddress");
                bool active = u.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.True;
                list.Add(new JiraUser(accountId, displayName, email, active));
            }
        }
        return list;
    }

    /// <summary>GET JSON từ Jira + map status code chuẩn (403/404/502).</summary>
    private async Task<JsonElement> GetJsonAsync(Connection connection, string url, CancellationToken ct)
    {
        var http = await BuildClientAsync(connection, ct);

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException($"Jira API lỗi kết nối: {ex.Message}", ex);
        }

        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    }

    private static string ApiBase(Connection connection) => string.Format(ApiBaseFormat, connection.ProviderAccountId);

    /// <summary>Gửi PUT/POST write tới Jira. 400 = field/transition không hợp lệ → BusinessRule (422).</summary>
    private async Task SendWriteAsync(Connection connection, HttpMethod method, string url, object body, CancellationToken ct)
    {
        var http = await BuildClientAsync(connection, ct);
        using var msg = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body)
        };

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(msg, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException($"Jira API lỗi kết nối: {ex.Message}", ex);
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var detail = await SafeReadBodyAsync(response, ct);
            throw new BusinessRuleException($"Jira từ chối thao tác (field/transition không hợp lệ): {detail}");
        }

        await EnsureSuccessAsync(response, ct);
    }

    private async Task<HttpClient> BuildClientAsync(Connection connection, CancellationToken ct)
    {
        var accessToken = await _tokenService.GetFreshAccessTokenAsync(connection, ct);
        var http = _httpClientFactory.CreateClient("Jira"); // Accept header đã cấu hình ở DI
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
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

    private static JiraSearchResult ParseSearch(JsonElement doc)
    {
        var issues = new List<JiraIssue>();

        if (doc.TryGetProperty("issues", out var issuesEl) && issuesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var issueEl in issuesEl.EnumerateArray())
                issues.Add(ParseIssue(issueEl));
        }

        string? nextPageToken = doc.TryGetProperty("nextPageToken", out var tokEl) && tokEl.ValueKind == JsonValueKind.String
            ? tokEl.GetString()
            : null;

        bool isLast = doc.TryGetProperty("isLast", out var lastEl) && lastEl.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? lastEl.GetBoolean()
            : nextPageToken is null;

        return new JiraSearchResult(issues, nextPageToken, isLast);
    }

    private static JiraIssue ParseIssue(JsonElement issueEl)
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

        // KHÔNG build issueUrl ở đây: browse URL của Jira Cloud là https://{site}.atlassian.net/browse/{KEY},
        // cần TÊN SITE — không phải cloudId (UUID). Connection chỉ lưu cloudId nên chưa dựng được link đúng.
        // Để null thay vì emit link sai (api.atlassian.com/.../browse → API error khi click). Site URL: phase sau.
        return new JiraIssue(
            id, key, projectKey, summary, description,
            statusName, assignee, priorityName, issueTypeName, null, updated);
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
