using System.Text.Json;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Mapping;

public class JiraItemMapper : IJiraItemMapper
{
    private const int SnippetMaxLength = 200;

    public Item ToItem(JiraIssue issue, Guid userId, Guid connectionId)
    {
        var description = AdfConverter.ToPlainText(issue.Description);
        var snippet = description.Length > SnippetMaxLength
            ? description[..SnippetMaxLength]
            : description;

        var metadata = new
        {
            issueKey = issue.Key,
            projectKey = issue.ProjectKey,
            status = issue.StatusName,
            assignee = issue.AssigneeDisplayName,
            priority = issue.PriorityName,
            issueType = issue.IssueTypeName,
            issueUrl = issue.IssueUrl
        };

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
            Status = ItemStatus.Inbox,
            OccurredAt = issue.Updated?.UtcDateTime ?? DateTime.UtcNow,
            IsImportant = false,
            IsArchived = false,
            MetadataJson = JsonSerializer.Serialize(metadata)
        };
    }
}
