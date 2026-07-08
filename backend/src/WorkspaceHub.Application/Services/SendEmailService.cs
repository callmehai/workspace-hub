using System.Text.Json;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

/// <summary>Gửi email trực tiếp qua Gmail (đồng bộ, không hẹn giờ). Validate connection thuộc user + Gmail + Active.</summary>
public class SendEmailService : ISendEmailService
{
    private readonly IConnectionRepository _connections;
    private readonly IGmailGateway _gmail;
    private readonly IGoogleContactRepository _googleContacts;
    private readonly IGoogleContactMapper _googleContactMapper;
    private readonly IItemRepository _items;

    public SendEmailService(
        IConnectionRepository connections,
        IGmailGateway gmail,
        IGoogleContactRepository googleContacts,
        IGoogleContactMapper googleContactMapper,
        IItemRepository items)
    {
        _connections = connections;
        _gmail = gmail;
        _googleContacts = googleContacts;
        _googleContactMapper = googleContactMapper;
        _items = items;
    }

    public async Task<SendEmailResult> SendAsync(Guid userId, SendEmailRequest request, CancellationToken ct = default)
    {
        var connection = await _connections.GetByIdTrackedAsync(request.ConnectionId, ct)
            ?? throw new NotFoundException("Connection", request.ConnectionId);

        if (connection.UserId != userId)
            throw new NotFoundException("Connection", request.ConnectionId);

        if (connection.ServiceType != ServiceType.Gmail)
            throw new BusinessRuleException("Only Gmail connections can be used to send emails.");

        if (connection.Status != ConnectionStatus.Active)
            throw new BusinessRuleException($"Connection is not active (status: {connection.Status}).");

        var messageId = await _gmail.SendMessageAsync(
            connection, request.To, request.Cc, request.Bcc, request.Subject, request.BodyHtml, ct);

        return new SendEmailResult(messageId, DateTime.UtcNow);
    }

    public async Task<string?> GetSignatureAsync(Guid userId, Guid connectionId, CancellationToken ct = default)
    {
        var connection = await _connections.GetByIdTrackedAsync(connectionId, ct)
            ?? throw new NotFoundException("Connection", connectionId);

        if (connection.UserId != userId)
            throw new NotFoundException("Connection", connectionId);

        if (connection.ServiceType != ServiceType.Gmail)
            throw new BusinessRuleException("Only Gmail connections have a signature.");

        return await _gmail.GetSignatureAsync(connection, ct);
    }

    public async Task<IQueryable<ContactSuggestionDto>> GetContactSuggestionsAsync(
        Guid userId, Guid connectionId, CancellationToken ct = default)
    {
        await ValidateGmailConnectionAsync(userId, connectionId, ct);
        return _googleContacts.GetByConnectionId(connectionId);
    }

    private async Task ValidateGmailConnectionAsync(Guid userId, Guid connectionId, CancellationToken ct)
    {
        var connection = await _connections.GetByIdAsync(connectionId, ct)
            ?? throw new NotFoundException("Connection", connectionId);

        if (connection.UserId != userId)
            throw new NotFoundException("Connection", connectionId);

        if (connection.ServiceType != ServiceType.Gmail)
            throw new BusinessRuleException("Only Gmail connections can be used for contact suggestions.");

        if (connection.Status != ConnectionStatus.Active)
            throw new BusinessRuleException($"Connection is not active (status: {connection.Status}).");
    }

    public async Task<EmailThreadResponse> GetThreadAsync(Guid userId, Guid itemId, CancellationToken ct = default)
    {
        var (connection, item) = await GetAndValidateConnectionAndItemAsync(userId, null, itemId, ct);

        var threadId = GetMetadataString(item.MetadataJson, "threadId");
        if (string.IsNullOrEmpty(threadId)) throw new BusinessRuleException("Item has no threadId in metadata.");

        var thread = await _gmail.GetThreadAsync(connection, threadId, ct);
        
        var messageDtos = thread.Messages.Select(m => new EmailThreadMessageDto(
            m.MessageId, m.From, m.To, m.Cc, m.Bcc, m.Subject, m.BodyHtml, m.BodyPlainText,
            m.OccurredAt?.UtcDateTime ?? DateTime.UtcNow, m.IsUnread, m.IsStarred, m.HasAttachment,
            m.Attachments.Select(a => new EmailAttachmentDto(a.AttachmentId, a.Filename, a.MimeType, a.Size)).ToList()
        )).ToList();

        return new EmailThreadResponse(thread.ThreadId, thread.Subject, messageDtos);
    }

    public async Task<SendInThreadResult> ReplyAsync(Guid userId, ReplyEmailRequest request, CancellationToken ct = default)
    {
        var (connection, item) = await GetAndValidateConnectionAndItemAsync(userId, request.ConnectionId, request.ItemId, ct);

        var threadId = GetMetadataString(item.MetadataJson, "threadId");
        if (string.IsNullOrEmpty(threadId)) throw new BusinessRuleException("Item has no threadId in metadata.");

        var inReplyTo = item.ExternalId;
        var subject = item.Title;
        if (!subject.StartsWith("Re: ", StringComparison.OrdinalIgnoreCase))
        {
            subject = "Re: " + subject;
        }

        var toList = new List<string>();
        var ccList = new List<string>();

        var fromOriginal = GetMetadataString(item.MetadataJson, "from");
        if (!string.IsNullOrEmpty(fromOriginal))
        {
            toList.Add(fromOriginal);
        }

        if (request.ReplyAll)
        {
            var originalTo = GetMetadataStringArray(item.MetadataJson, "to");
            var originalCc = GetMetadataStringArray(item.MetadataJson, "cc");
            
            var me = connection.ProviderAccountId;
            
            var allTo = originalTo.Where(x => !x.Contains(me, StringComparison.OrdinalIgnoreCase));
            var allCc = originalCc.Where(x => !x.Contains(me, StringComparison.OrdinalIgnoreCase));
            
            toList.AddRange(allTo);
            ccList.AddRange(allCc);
        }

        if (request.Cc != null) ccList.AddRange(request.Cc);
        var bccList = request.Bcc ?? new List<string>();

        // Lấy Cc live từ Gmail API nếu email cũ chưa lưu Cc trong metadata (Cách A đã chốt)
        if (request.ReplyAll && !item.MetadataJson.Contains("\"cc\""))
        {
             var liveMsg = await _gmail.GetMessageAsync(connection, item.ExternalId, ct);
             var me = connection.ProviderAccountId;
             ccList.AddRange(liveMsg.Cc.Where(x => !x.Contains(me, StringComparison.OrdinalIgnoreCase)));
             toList.AddRange(liveMsg.To.Where(x => !x.Contains(me, StringComparison.OrdinalIgnoreCase) && !toList.Contains(x)));
        }

        // Loại bỏ trùng lặp và loại trừ tài khoản của mình khỏi danh sách nhận nếu bị dính
        var myEmail = connection.ProviderAccountId;
        toList = toList.Where(x => !x.Contains(myEmail, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ccList = ccList.Where(x => !x.Contains(myEmail, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Nếu gửi cho chính mình từ một luồng mà không có ai khác
        if (toList.Count == 0 && !string.IsNullOrEmpty(fromOriginal))
        {
            toList.Add(fromOriginal);
        }

        var messageId = await _gmail.SendInThreadAsync(connection, threadId, inReplyTo, toList, ccList, bccList, subject, request.BodyHtml, null, ct);
        
        return new SendInThreadResult(messageId, threadId, DateTime.UtcNow);
    }

    public async Task<SendInThreadResult> ForwardAsync(Guid userId, ForwardEmailRequest request, CancellationToken ct = default)
    {
        var (connection, item) = await GetAndValidateConnectionAndItemAsync(userId, request.ConnectionId, request.ItemId, ct);

        var threadId = GetMetadataString(item.MetadataJson, "threadId");
        if (string.IsNullOrEmpty(threadId)) throw new BusinessRuleException("Item has no threadId in metadata.");

        var subject = item.Title;
        if (!subject.StartsWith("Fwd: ", StringComparison.OrdinalIgnoreCase))
        {
            subject = "Fwd: " + subject;
        }

        // Lấy live body và attachments từ email gốc
        var liveThread = await _gmail.GetThreadAsync(connection, threadId, ct);
        var originalMessage = liveThread.Messages.FirstOrDefault(m => m.MessageId == item.ExternalId);
        if (originalMessage == null) throw new BusinessRuleException("Original message not found in thread.");

        var bodyGoc = originalMessage.BodyHtml ?? originalMessage.BodyPlainText?.Replace("\n", "<br/>") ?? "";

        var fwdBody = request.BodyHtml + "<br/><br/>---------- Forwarded message ----------<br/>" + 
            $"From: {originalMessage.From}<br/>" +
            $"Date: {originalMessage.OccurredAt?.ToString("f")}<br/>" +
            $"Subject: {originalMessage.Subject}<br/>" +
            $"To: {string.Join(", ", originalMessage.To)}<br/><br/>" +
            bodyGoc;

        var attachmentsData = new List<Application.Abstractions.GmailAttachmentData>();
        if (request.IncludeAttachments && originalMessage.Attachments.Count > 0)
        {
            foreach (var attInfo in originalMessage.Attachments)
            {
                var attData = await _gmail.GetAttachmentAsync(connection, originalMessage.MessageId, attInfo.AttachmentId, attInfo.Filename, attInfo.MimeType, ct);
                attachmentsData.Add(attData);
            }
        }

        var messageId = await _gmail.SendInThreadAsync(connection, threadId, null, request.To, request.Cc, request.Bcc, subject, fwdBody, attachmentsData, ct);

        return new SendInThreadResult(messageId, threadId, DateTime.UtcNow);
    }

    public async Task<Application.Abstractions.GmailAttachmentData> GetAttachmentAsync(Guid userId, Guid itemId, string attachmentId, CancellationToken ct = default)
    {
        var (connection, item) = await GetAndValidateConnectionAndItemAsync(userId, null, itemId, ct);

        // Lấy metadata live
        var threadId = GetMetadataString(item.MetadataJson, "threadId");
        if (string.IsNullOrEmpty(threadId)) throw new BusinessRuleException("Item has no threadId in metadata.");

        var thread = await _gmail.GetThreadAsync(connection, threadId, ct);
        var msg = thread.Messages.FirstOrDefault(m => m.MessageId == item.ExternalId);
        if (msg == null) throw new BusinessRuleException("Message not found in thread.");
        
        var attInfo = msg.Attachments.FirstOrDefault(a => a.AttachmentId == attachmentId);
        if (attInfo == null) throw new NotFoundException("Attachment", attachmentId);

        return await _gmail.GetAttachmentAsync(connection, msg.MessageId, attachmentId, attInfo.Filename, attInfo.MimeType, ct);
    }

    // ───────────────────────── Private Helpers ─────────────────────────

    private async Task<(Connection connection, Domain.Entities.Item item)> GetAndValidateConnectionAndItemAsync(Guid userId, Guid? connectionId, Guid itemId, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(itemId, ct)
            ?? throw new NotFoundException("Item", itemId);

        if (item.UserId != userId)
            throw new NotFoundException("Item", itemId);
            
        if (item.ConnectionId == null)
            throw new BusinessRuleException("Item has no connection.");

        if (connectionId.HasValue && item.ConnectionId != connectionId)
            throw new BusinessRuleException("Item does not belong to the specified connection.");

        var connection = await _connections.GetByIdTrackedAsync(item.ConnectionId.Value, ct)
            ?? throw new NotFoundException("Connection", item.ConnectionId.Value);

        if (connection.UserId != userId)
            throw new NotFoundException("Connection", item.ConnectionId.Value);

        if (connection.ServiceType != ServiceType.Gmail)
            throw new BusinessRuleException("Only Gmail connections can be used.");

        if (connection.Status != ConnectionStatus.Active)
            throw new BusinessRuleException($"Connection is not active (status: {connection.Status}).");

        return (connection, item);
    }

    private string? GetMetadataString(string? metadataJson, string propertyName)
    {
        if (string.IsNullOrEmpty(metadataJson)) return null;
        try
        {
            var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.TryGetProperty(propertyName, out var prop))
            {
                return prop.GetString();
            }
        }
        catch { }
        return null;
    }

    private List<string> GetMetadataStringArray(string? metadataJson, string propertyName)
    {
        if (string.IsNullOrEmpty(metadataJson)) return new List<string>();
        try
        {
            var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var item in prop.EnumerateArray())
                {
                    var val = item.GetString();
                    if (!string.IsNullOrEmpty(val)) list.Add(val);
                }
                return list;
            }
        }
        catch { }
        return new List<string>();
    }
}
