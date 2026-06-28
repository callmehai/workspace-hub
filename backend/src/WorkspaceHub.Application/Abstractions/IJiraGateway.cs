using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Abstractions;

/// <summary>
/// Cổng gọi Jira REST API (Atlassian Cloud). Cài đặt ở Infrastructure.
/// Base URL theo cloudId của connection: https://api.atlassian.com/ex/jira/{cloudId}/rest/api/3.
/// </summary>
public interface IJiraGateway
{
    /// <summary>
    /// Search issue qua JQL (POST /rest/api/3/search/jql), phân trang bằng nextPageToken.
    /// jql null/empty → dùng JQL mặc định (issue user là assignee/reporter).
    /// </summary>
    Task<JiraSearchResult> SearchIssuesAsync(
        Connection connection,
        string? jql,
        string? pageToken,
        int maxResults,
        CancellationToken ct = default);

    /// <summary>
    /// Tạo issue mới (POST /rest/api/3/issue). Trả về id + key của issue vừa tạo.
    /// description (plain text) được convert sang ADF trước khi gửi.
    /// </summary>
    Task<JiraCreatedIssue> CreateIssueAsync(
        Connection connection,
        CreateJiraIssueRequest request,
        CancellationToken ct = default);

    /// <summary>Lấy 1 issue đầy đủ field (GET /rest/api/3/issue/{idOrKey}) để map sang Item sau khi tạo.</summary>
    Task<JiraIssue> GetIssueAsync(
        Connection connection,
        string issueIdOrKey,
        CancellationToken ct = default);
}
