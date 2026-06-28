using System.Text.Json;

namespace WorkspaceHub.Application.Abstractions;

/// <summary>
/// 1 issue Jira đã chuẩn hoá từ REST response (Application không phụ thuộc shape raw của Atlassian).
/// Description giữ nguyên ADF (JsonElement) — convert sang text/markdown ở mapper.
/// Updated = <c>fields.updated</c>: vừa là OccurredAt vừa là version-token cho conflict (Jira không trả HTTP ETag).
/// </summary>
public record JiraIssue(
    string Id,
    string Key,
    string? ProjectKey,
    string? Summary,
    JsonElement? Description,
    string? StatusName,
    string? AssigneeDisplayName,
    string? PriorityName,
    string? IssueTypeName,
    string? IssueUrl,
    DateTimeOffset? Updated);

/// <summary>
/// Kết quả 1 trang search JQL. NextPageToken null = hết trang (API mới /search/jql dùng token thay startAt).
/// </summary>
public record JiraSearchResult(
    IReadOnlyList<JiraIssue> Issues,
    string? NextPageToken,
    bool IsLast);

/// <summary>
/// Payload tạo issue gửi xuống gateway. Description là plain text — gateway convert sang ADF.
/// Assignee = accountId; Priority/IssueType = tên hiển thị; Labels không chứa khoảng trắng (ràng buộc Jira).
/// </summary>
public record CreateJiraIssueRequest(
    string ProjectKey,
    string IssueType,
    string Summary,
    string? Description = null,
    string? AssigneeAccountId = null,
    string? PriorityName = null,
    IReadOnlyList<string>? Labels = null);

/// <summary>Kết quả thô của POST /rest/api/3/issue.</summary>
public record JiraCreatedIssue(string Id, string Key);

/// <summary>
/// Payload update issue (SCRUM-57). Chỉ field nào != null mới gửi lên Jira.
/// Summary/Description sửa được (khác Email). Description plain text → ADF ở gateway.
/// Assignee/Priority/StatusTransition/Comment xử lý qua endpoint riêng — KHÔNG nằm trong PUT fields.
/// </summary>
public record UpdateJiraIssueRequest(
    string? Summary = null,
    string? Description = null,
    string? PriorityName = null,
    IReadOnlyList<string>? Labels = null);

/// <summary>1 transition khả dụng của issue (đổi status). Id dùng để POST transition.</summary>
public record JiraTransition(string Id, string Name, string? ToStatusName);
