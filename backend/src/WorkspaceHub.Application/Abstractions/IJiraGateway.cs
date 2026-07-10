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

    /// <summary>Thêm comment (POST .../comment). body markdown subset → ADF; mediaIds → nhúng attachment. Trả comment vừa tạo.</summary>
    Task<JiraComment> AddCommentAsync(
        Connection connection,
        string issueIdOrKey,
        string commentBody,
        IEnumerable<string>? mediaIds = null,
        CancellationToken ct = default);

    /// <summary>List comment của issue (GET .../comment). Body ADF → plain text.</summary>
    Task<IReadOnlyList<JiraComment>> GetCommentsAsync(Connection connection, string issueIdOrKey, CancellationToken ct = default);

    /// <summary>Sửa comment (PUT .../comment/{id}). body plain text → ADF.</summary>
    Task<JiraComment> UpdateCommentAsync(Connection connection, string issueIdOrKey, string commentId, string commentBody, CancellationToken ct = default);

    /// <summary>Xoá comment (DELETE .../comment/{id}).</summary>
    Task DeleteCommentAsync(Connection connection, string issueIdOrKey, string commentId, CancellationToken ct = default);

    // ───────────────────── Attachment (2 chiều) ─────────────────────

    /// <summary>List attachment metadata của issue (GET issue?fields=attachment).</summary>
    Task<IReadOnlyList<JiraAttachment>> GetAttachmentsAsync(Connection connection, string issueIdOrKey, CancellationToken ct = default);

    /// <summary>Tải nội dung attachment (GET /attachment/content/{id}).</summary>
    Task<JiraAttachmentContent> DownloadAttachmentAsync(Connection connection, string attachmentId, string filename, string mimeType, CancellationToken ct = default);

    /// <summary>Upload attachment lên issue (POST issue/{key}/attachments, multipart, header X-Atlassian-Token).</summary>
    Task<IReadOnlyList<JiraAttachment>> UploadAttachmentAsync(Connection connection, string issueIdOrKey, string filename, string mimeType, byte[] data, CancellationToken ct = default);

    /// <summary>Xoá attachment (DELETE /attachment/{id}).</summary>
    Task DeleteAttachmentAsync(Connection connection, string attachmentId, CancellationToken ct = default);

    /// <summary>Xoá issue (DELETE /rest/api/3/issue/{key}?deleteSubtasks=true). Thiếu quyền → Forbidden(403).</summary>
    Task DeleteIssueAsync(
        Connection connection,
        string issueIdOrKey,
        CancellationToken ct = default);

    // ───────────────────── Metadata helpers (SCRUM-59) ─────────────────────

    /// <summary>List project user truy cập được (GET /rest/api/3/project/search).</summary>
    Task<IReadOnlyList<JiraProject>> GetProjectsAsync(Connection connection, CancellationToken ct = default);

    /// <summary>Issue type hợp lệ của 1 project (GET /rest/api/3/project/{key} → issueTypes).</summary>
    Task<IReadOnlyList<JiraIssueType>> GetIssueTypesAsync(Connection connection, string projectKey, CancellationToken ct = default);

    /// <summary>Danh sách priority (GET /rest/api/3/priority).</summary>
    Task<IReadOnlyList<JiraPriority>> GetPrioritiesAsync(Connection connection, CancellationToken ct = default);

    /// <summary>User gán được cho 1 project (GET /rest/api/3/user/assignable/search), lọc theo query nếu có.</summary>
    Task<IReadOnlyList<JiraUser>> GetAssignableUsersAsync(Connection connection, string projectKey, string? query, CancellationToken ct = default);

    /// <summary>
    /// Site URL (vd https://xxx.atlassian.net) của connection — tra từ accessible-resources theo cloudId.
    /// Dùng để build browse URL "{site}/browse/{KEY}". Trả null nếu không lấy được (không throw).
    /// </summary>
    Task<string?> GetSiteUrlAsync(Connection connection, CancellationToken ct = default);

    /// <summary>
    /// Thông tin Jira site (name + url) của connection — cho FE hiển thị tên account thay cho cloudId.
    /// Trả null nếu không lấy được (không throw).
    /// </summary>
    Task<JiraSite?> GetSiteAsync(Connection connection, CancellationToken ct = default);
}
