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

    // ───────────────────── Write-back (SCRUM-57) ─────────────────────

    /// <summary>Update field issue (PUT /rest/api/3/issue/{key}): summary, description(→ADF), priority, labels.</summary>
    Task UpdateIssueAsync(
        Connection connection,
        string issueIdOrKey,
        UpdateJiraIssueRequest request,
        CancellationToken ct = default);

    /// <summary>Đổi assignee (PUT /rest/api/3/issue/{key}/assignee). accountId rỗng/"-1" = unassign.</summary>
    Task AssignIssueAsync(
        Connection connection,
        string issueIdOrKey,
        string? accountId,
        CancellationToken ct = default);

    /// <summary>Danh sách transition khả dụng của issue (GET .../transitions) — để map tên→id và validate.</summary>
    Task<IReadOnlyList<JiraTransition>> GetTransitionsAsync(
        Connection connection,
        string issueIdOrKey,
        CancellationToken ct = default);

    /// <summary>Thực hiện 1 transition (POST .../transitions) — đổi status. transitionId từ GetTransitions.</summary>
    Task TransitionIssueAsync(
        Connection connection,
        string issueIdOrKey,
        string transitionId,
        CancellationToken ct = default);

    /// <summary>Thêm comment (POST .../comment). body plain text → ADF.</summary>
    Task AddCommentAsync(
        Connection connection,
        string issueIdOrKey,
        string commentBody,
        CancellationToken ct = default);

    /// <summary>Xoá issue (DELETE /rest/api/3/issue/{key}?deleteSubtasks=true). Thiếu quyền → Forbidden(403).</summary>
    Task DeleteIssueAsync(
        Connection connection,
        string issueIdOrKey,
        CancellationToken ct = default);
}
