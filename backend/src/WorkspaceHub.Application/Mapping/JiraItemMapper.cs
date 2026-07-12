using System.Globalization;
using System.Text.Json;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Mapping;

public class JiraItemMapper : IJiraItemMapper
{
    private const int SnippetMaxLength = 200;

    public Item ToItem(JiraIssue issue, Guid userId, Guid connectionId, string? siteUrl = null)
    {
        // Markdown subset (giữ heading/bold/list/code như trên Jira) — FE render bằng miniMarkdown.
        var description = AdfConverter.ToMarkdown(issue.Description);
        // Snippet cho list = plain text (không lộ ký tự markdown ** ` # trong dòng preview).
        var plainForSnippet = AdfConverter.ToPlainText(issue.Description);
        var snippet = plainForSnippet.Length > SnippetMaxLength
            ? plainForSnippet[..SnippetMaxLength]
            : plainForSnippet;

        // Browse URL mở issue trên web: "{site}/browse/{KEY}" (site lấy từ accessible-resources).
        var issueUrl = !string.IsNullOrWhiteSpace(siteUrl) && !string.IsNullOrWhiteSpace(issue.Key)
            ? $"{siteUrl}/browse/{issue.Key}"
            : issue.IssueUrl;

        var (occurredAt, dueAt, dueDateIso) = MapDueCalendarFields(issue.DueDate, issue.Updated);

        var metadata = new
        {
            issueKey = issue.Key,
            projectKey = issue.ProjectKey,
            projectName = issue.ProjectName,
            status = issue.StatusName,
            assignee = issue.AssigneeDisplayName,
            assigneeAccountId = issue.AssigneeAccountId,
            priority = issue.PriorityName,
            issueType = issue.IssueTypeName,
            description,           // description ĐẦY ĐỦ cho drawer (Snippet chỉ 200 ký tự cho list)
            issueUrl,
            dueDate = dueDateIso
        };

        var mappedStatus = ItemStatus.Inbox;
        
        if (!string.IsNullOrEmpty(issue.StatusCategoryKey))
        {
            mappedStatus = issue.StatusCategoryKey.ToLower() switch
            {
                "new" => ItemStatus.Inbox,
                "indeterminate" => ItemStatus.Doing,
                "done" => ItemStatus.Done,
                _ => ItemStatus.Inbox
            };
        }
        else
        {
            var lowerStatus = issue.StatusName?.ToLower() ?? "";
            if (lowerStatus.Contains("done") || lowerStatus.Contains("xong") || lowerStatus.Contains("hoàn thành") || lowerStatus.Contains("closed"))
            {
                mappedStatus = ItemStatus.Done;
            }
            else if (lowerStatus.Contains("progress") || lowerStatus.Contains("doing") || lowerStatus.Contains("đang") || lowerStatus.Contains("review") || lowerStatus.Contains("test"))
            {
                mappedStatus = ItemStatus.Doing;
            }
        }

        return new Item
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = ItemType.Ticket,
            Title = string.IsNullOrWhiteSpace(issue.Summary) ? "(Không có tiêu đề)" : issue.Summary,
            Snippet = snippet,
            ExternalId = issue.Id,
            ConnectionId = connectionId,
            // Jira không trả HTTP ETag — dùng fields.updated làm version-token cho conflict (SCRUM-57).
            ETag = issue.Updated?.UtcDateTime.ToString("O"),
            Status = mappedStatus,
            OccurredAt = occurredAt,
            DueAt = dueAt,
            IsImportant = false,
            IsArchived = false,
            MetadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
        };
    }

    /// <summary>
    /// Ticket có deadline → OccurredAt/DueAt cho calendar overlap (all-day: end exclusive +1 ngày).
    /// Không có deadline → OccurredAt = updated (list/Kanban), DueAt null (FE calendar bỏ qua).
    /// </summary>
    private static (DateTime OccurredAt, DateTime? DueAt, string? DueDateIso) MapDueCalendarFields(
        DateTimeOffset? dueDate,
        DateTimeOffset? updated)
    {
        if (!dueDate.HasValue)
        {
            return (updated?.UtcDateTime ?? DateTime.UtcNow, null, null);
        }

        var dueUtc = dueDate.Value.UtcDateTime;
        var start = new DateTime(dueUtc.Year, dueUtc.Month, dueUtc.Day, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddDays(1);
        var iso = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return (start, end, iso);
    }
}
