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
