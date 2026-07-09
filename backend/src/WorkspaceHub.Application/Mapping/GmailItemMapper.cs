using System.Text.Json;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Mapping;

public class GmailItemMapper : IGmailItemMapper
{
    public Item ToItem(GmailMessage message, Guid userId, Guid connectionId, ISet<string> importantEmails)
    {
        bool isImportant = false;
        if (!string.IsNullOrWhiteSpace(message.From))
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(message.From);
                if (importantEmails.Contains(addr.Address))
                {
                    isImportant = true;
                }
            }
            catch
            {
                // Bỏ qua nếu không parse được email
            }
        }

        var metadata = new
        {
            from = message.From,
            to = message.To,
            cc = message.Cc,
            bcc = message.Bcc,
            threadId = message.ThreadId,
            labels = message.LabelIds,
            hasAttachment = message.HasAttachment,
            rfc822MessageId = message.Rfc822MessageId,
            webUrl = $"https://mail.google.com/mail/u/0/#all/{message.Id}",
            isUnread = message.LabelIds != null && message.LabelIds.Contains("UNREAD")
        };

        return new Item
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = ItemType.Email,
            Title = message.Subject ?? "(Không có tiêu đề)",
            Snippet = message.Snippet ?? string.Empty,
            ExternalId = message.Id,
            ThreadId = message.ThreadId,
            ConnectionId = connectionId,
            Status = ItemStatus.Inbox,
            OccurredAt = message.OccurredAt?.UtcDateTime ?? DateTime.UtcNow,
            IsImportant = isImportant,
            IsArchived = false,
            MetadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            ETag = message.ETag
        };
    }
}
