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
            projectName = issue.ProjectName,
            status = issue.StatusName,
            assignee = issue.AssigneeDisplayName,
            assigneeAccountId = issue.AssigneeAccountId,
            priority = issue.PriorityName,
            issueType = issue.IssueTypeName,
            issueUrl = issue.IssueUrl
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
            OccurredAt = issue.Updated?.UtcDateTime ?? DateTime.UtcNow,
            IsImportant = false,
            IsArchived = false,
            MetadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
        };
    }
}
