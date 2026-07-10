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
    string? ProjectName,
    string? Summary,
    JsonElement? Description,
    string? StatusName,
    string? AssigneeDisplayName,
    string? AssigneeAccountId,
    string? PriorityName,
    string? IssueTypeName,
    string? IssueUrl,
    DateTimeOffset? Updated,
    string? StatusCategoryKey = null);

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
    IReadOnlyList<string>? Labels = null,
    string? IssueTypeName = null);

/// <summary>1 transition khả dụng của issue (đổi status). Id dùng để POST transition.</summary>
public record JiraTransition(string Id, string Name, string? ToStatusName);

// ───────────────────── Metadata helpers (SCRUM-59) ─────────────────────

/// <summary>1 project Jira (cho dropdown chọn project khi tạo issue).</summary>
public record JiraProject(string Id, string Key, string Name);

/// <summary>1 issue type của project (Task/Bug/Story...).</summary>
public record JiraIssueType(string Id, string Name, bool Subtask);

/// <summary>1 priority Jira (High/Medium/Low...).</summary>
public record JiraPriority(string Id, string Name);

/// <summary>1 user gán được cho issue/project (cho dropdown assignee). AccountId dùng khi assign.
/// AvatarUrl = ảnh đại diện (48x48) từ Jira; Email thường rỗng do quyền riêng tư Atlassian.</summary>
public record JiraUser(string AccountId, string DisplayName, string? Email, bool Active, string? AvatarUrl = null);

/// <summary>Thông tin Jira site (Atlassian) của 1 connection — cho FE hiển thị tên account thay cho cloudId.
/// Name = tên site (vd "Trustsoft"); Url = "https://xxx.atlassian.net".</summary>
public record JiraSite(string Name, string Url);

// ───────────────────── Comment + Attachment (2 chiều) ─────────────────────

/// <summary>1 comment của issue. Body = plain text (đã convert từ ADF). CanEdit/CanDelete: là tác giả.</summary>
public record JiraComment(
    string Id,
    string BodyText,
    string AuthorName,
    string? AuthorAccountId,
    DateTimeOffset? Created,
    DateTimeOffset? Updated);

/// <summary>1 attachment của issue (metadata). Content tải qua DownloadAttachmentAsync theo Id.</summary>
public record JiraAttachment(
    string Id,
    string Filename,
    string? MimeType,
    long Size,
    string? AuthorName,
    DateTimeOffset? Created,
    string? ContentUrl = null);

/// <summary>Nội dung 1 attachment đã tải về (binary).</summary>
public record JiraAttachmentContent(byte[] Data, string MimeType, string Filename);
