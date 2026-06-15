using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Application.Abstractions;

namespace WorkspaceHub.Application.Mapping;

public interface IGmailItemMapper
{
    Item ToItem(GmailMessage message, Guid userId, Guid connectionId, ISet<string> importantEmails);
}
