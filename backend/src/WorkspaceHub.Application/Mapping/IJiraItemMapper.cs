using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Mapping;

public interface IJiraItemMapper
{
    /// <summary>Map 1 issue Jira sang Item (Type=Ticket). ETag = fields.updated (version-token).</summary>
    Item ToItem(JiraIssue issue, Guid userId, Guid connectionId);
}
