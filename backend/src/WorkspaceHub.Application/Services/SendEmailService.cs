using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Application.Mapping;

namespace WorkspaceHub.Application.Services;

/// <summary>Gửi email trực tiếp qua Gmail (đồng bộ, không hẹn giờ). Validate connection thuộc user + Gmail + Active.</summary>
public class SendEmailService : ISendEmailService
{
    private readonly IConnectionRepository _connections;
    private readonly IGmailGateway _gmail;
    private readonly IGoogleContactRepository _googleContacts;
    private readonly IGoogleContactMapper _googleContactMapper;
    private readonly IItemRepository _items;
    private readonly IFolderRepository _folders;
    private readonly ILogger<SendEmailService> _logger;

    public SendEmailService(
        IConnectionRepository connections,
        IGmailGateway gmail,
        IGoogleContactRepository googleContacts,
        IGoogleContactMapper googleContactMapper,
        IItemRepository items,
        IFolderRepository folders,
        ILogger<SendEmailService> logger)
    {
        _connections = connections;
        _gmail = gmail;
        _googleContacts = googleContacts;
        _googleContactMapper = googleContactMapper;
        _items = items;
        _folders = folders;
        _logger = logger;
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

        var attachments = DecodeAttachments(request.Attachments);

        var messageId = await _gmail.SendMessageAsync(
            connection, request.To, request.Cc, request.Bcc, request.Subject, request.BodyHtml,
            attachments.Count > 0 ? attachments : null, ct);

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
        {
            var hasAccess = await _folders.IsConnectionSharedWithUserAsEditorAsync(connectionId, userId, ct);
            if (!hasAccess)
                throw new NotFoundException("Connection", connectionId);
        }

        if (connection.ServiceType != ServiceType.Gmail)
            throw new BusinessRuleException("Only Gmail connections can be used for contact suggestions.");

        if (connection.Status != ConnectionStatus.Active)
            throw new BusinessRuleException($"Connection is not active (status: {connection.Status}).");
    }

    public async Task<EmailThreadResponse> GetThreadAsync(Guid userId, Guid itemId, CancellationToken ct = default)
    {
        var (connection, item) = await GetAndValidateConnectionAndItemAsync(userId, null, itemId, ct, requireEditor: false);

        var threadId = GetMetadataString(item.MetadataJson, "threadId");
        if (string.IsNullOrEmpty(threadId)) throw new BusinessRuleException("Item has no threadId in metadata.");

        var thread = await _gmail.GetThreadAsync(connection, threadId, ct);
        
        var localItems = await _items.GetTrackedByConnectionIdAsync(connection.Id, ct);

        var messageDtos = thread.Messages.Select(m => {
            Guid? localId = localItems.TryGetValue(m.MessageId, out var localItem) ? localItem.Id : (Guid?)null;
            
            var bodyHtml = m.BodyHtml;
            var to = m.To;
            var cc = m.Cc;
            var bcc = m.Bcc;
            var subject = m.Subject;

            if (localItem != null)
            {
                try
                {
                    var localMeta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(localItem.MetadataJson ?? "{}");
                    if (localMeta != null)
                    {
                        if (string.IsNullOrEmpty(bodyHtml) && localMeta.TryGetValue("bodyHtml", out var bh) && bh.ValueKind == JsonValueKind.String)
                        {
                            bodyHtml = bh.GetString();
                        }
                        if (string.IsNullOrEmpty(subject) && localMeta.TryGetValue("subject", out var sbj) && sbj.ValueKind == JsonValueKind.String)
                        {
                            subject = sbj.GetString();
                        }
                        if ((to == null || to.Count == 0) && localMeta.TryGetValue("to", out var tVal) && tVal.ValueKind == JsonValueKind.Array)
                        {
                            to = JsonSerializer.Deserialize<List<string>>(tVal.GetRawText()) ?? new List<string>();
                        }
                        if ((cc == null || cc.Count == 0) && localMeta.TryGetValue("cc", out var cVal) && cVal.ValueKind == JsonValueKind.Array)
                        {
                            cc = JsonSerializer.Deserialize<List<string>>(cVal.GetRawText()) ?? new List<string>();
                        }
                        if ((bcc == null || bcc.Count == 0) && localMeta.TryGetValue("bcc", out var bVal) && bVal.ValueKind == JsonValueKind.Array)
                        {
                            bcc = JsonSerializer.Deserialize<List<string>>(bVal.GetRawText()) ?? new List<string>();
                        }
                    }
                }
                catch
                {
                    // Ignore JSON parsing errors
                }
            }

            return new EmailThreadMessageDto(
                m.MessageId, m.From, to ?? new List<string>(), cc ?? new List<string>(), bcc ?? new List<string>(),
                subject, bodyHtml, m.BodyPlainText,
                m.OccurredAt?.UtcDateTime ?? DateTime.UtcNow, m.IsUnread, m.IsStarred, m.HasAttachment,
                m.Labels,
                m.Attachments.Select(a => new EmailAttachmentDto(a.AttachmentId, a.Filename, a.MimeType, a.Size)).ToList(),
                localId
            );
        }).ToList();

        return new EmailThreadResponse(thread.ThreadId, thread.Subject, messageDtos);
    }

    public async Task<SendInThreadResult> ReplyAsync(Guid userId, ReplyEmailRequest request, CancellationToken ct = default)
    {
        var (connection, item) = await GetAndValidateConnectionAndItemAsync(userId, request.ConnectionId, request.ItemId, ct, requireEditor: true);

        var threadId = GetMetadataString(item.MetadataJson, "threadId");
        if (string.IsNullOrEmpty(threadId)) throw new BusinessRuleException("Item has no threadId in metadata.");

        // Lấy RFC 5322 Message-ID header thật (dạng <xxx@mail.gmail.com>) để In-Reply-To/References chuẩn
        var inReplyTo = GetMetadataString(item.MetadataJson, "rfc822MessageId") ?? item.ExternalId;
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
            
            var allTo = originalTo.Where(x => !ExtractEmail(x).Equals(me, StringComparison.OrdinalIgnoreCase));
            var allCc = originalCc.Where(x => !ExtractEmail(x).Equals(me, StringComparison.OrdinalIgnoreCase));
            
            toList.AddRange(allTo);
            ccList.AddRange(allCc);
        }

        if (request.Cc != null) ccList.AddRange(request.Cc);
        var bccList = request.Bcc ?? new List<string>();

        // Lấy Cc live từ Gmail API nếu email cũ chưa lưu Cc trong metadata (Cách A đã chốt)
        if (request.ReplyAll && !item.MetadataJson.Contains("\"cc\""))
        {
             var externalId = item.ExternalId
                 ?? throw new BusinessRuleException("Item has no externalId.");
             var liveMsg = await _gmail.GetMessageAsync(connection, externalId, ct);
             var me = connection.ProviderAccountId;
             ccList.AddRange(liveMsg.Cc.Where(x => !ExtractEmail(x).Equals(me, StringComparison.OrdinalIgnoreCase)));
             toList.AddRange(liveMsg.To.Where(x => !ExtractEmail(x).Equals(me, StringComparison.OrdinalIgnoreCase) && !toList.Any(t => ExtractEmail(t).Equals(ExtractEmail(x), StringComparison.OrdinalIgnoreCase))));
        }

        // Loại bỏ trùng lặp và loại trừ tài khoản của mình khỏi danh sách nhận nếu bị dính
        var myEmail = connection.ProviderAccountId;
        toList = toList.Where(x => !ExtractEmail(x).Equals(myEmail, StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => ExtractEmail(x), StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        ccList = ccList.Where(x => !ExtractEmail(x).Equals(myEmail, StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => ExtractEmail(x), StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

        // Nếu gửi cho chính mình từ một luồng mà không có ai khác
        if (toList.Count == 0 && !string.IsNullOrEmpty(fromOriginal))
        {
            toList.Add(fromOriginal);
        }

        var attachments = DecodeAttachments(request.Attachments);

        var messageId = await _gmail.SendInThreadAsync(connection, threadId, inReplyTo, toList, ccList, bccList, subject, request.BodyHtml,
            attachments.Count > 0 ? attachments : null, ct);
        
        return new SendInThreadResult(messageId, threadId, DateTime.UtcNow);
    }

    public async Task<SendInThreadResult> ForwardAsync(Guid userId, ForwardEmailRequest request, CancellationToken ct = default)
    {
        var (connection, item) = await GetAndValidateConnectionAndItemAsync(userId, request.ConnectionId, request.ItemId, ct, requireEditor: true);

        var threadId = GetMetadataString(item.MetadataJson, "threadId");
        if (string.IsNullOrEmpty(threadId)) throw new BusinessRuleException("Item has no threadId in metadata.");

        var subject = item.Title;
        if (!subject.StartsWith("Fwd: ", StringComparison.OrdinalIgnoreCase))
        {
            subject = "Fwd: " + subject;
        }

        // Lấy live message gốc (chỉ 1 message, không cần cả thread)
        var forwardExternalId = item.ExternalId
            ?? throw new BusinessRuleException("Item has no externalId.");
        var liveMsg = await _gmail.GetMessageAsync(connection, forwardExternalId, ct);

        var bodyGoc = liveMsg.BodyHtml ?? liveMsg.BodyPlain?.Replace("\n", "<br/>") ?? "";

        var fwdBody = request.BodyHtml + "<br/><br/>---------- Forwarded message ----------<br/>" + 
            $"From: {liveMsg.From}<br/>" +
            $"Date: {liveMsg.OccurredAt?.ToString("f")}<br/>" +
            $"Subject: {liveMsg.Subject}<br/>" +
            $"To: {string.Join(", ", liveMsg.To)}<br/><br/>" +
            bodyGoc;

        var attachmentsData = new List<Application.Abstractions.GmailAttachmentData>();
        if (request.IncludeAttachments && liveMsg.HasAttachment)
        {
            // Lấy attachment metadata từ thread (cần payload detail) để có attachmentId
            var liveThread = await _gmail.GetThreadAsync(connection, threadId, ct);
            var threadMsg = liveThread.Messages.FirstOrDefault(m => m.MessageId == item.ExternalId);
            if (threadMsg != null)
            {
                foreach (var attInfo in threadMsg.Attachments)
                {
                    var attData = await _gmail.GetAttachmentAsync(connection, threadMsg.MessageId, attInfo.AttachmentId, attInfo.Filename, attInfo.MimeType, ct);
                    attachmentsData.Add(attData);
                }
            }
        }

        // Thêm file user tự đính kèm (ngoài file gốc)
        attachmentsData.AddRange(DecodeAttachments(request.Attachments));

        var messageId = await _gmail.SendInThreadAsync(connection, threadId, null, request.To, request.Cc, request.Bcc, subject, fwdBody,
            attachmentsData.Count > 0 ? attachmentsData : null, ct);

        return new SendInThreadResult(messageId, threadId, DateTime.UtcNow);
    }

    public async Task<Application.Abstractions.GmailAttachmentData> GetAttachmentAsync(
        Guid userId, Guid itemId, string messageId, string attachmentId,
        string? filename = null, string? mimeType = null, CancellationToken ct = default)
    {
        var (connection, _) = await GetAndValidateConnectionAndItemAsync(userId, null, itemId, ct, requireEditor: false);

        // Gmail cấp attachmentId MỚI mỗi lần đọc message/thread, nhưng id cũ vẫn hợp lệ với
        // attachments.get. Vì vậy KHÔNG re-fetch thread để so khớp id (id sẽ lệch → 404 giả);
        // dùng thẳng messageId + attachmentId client gửi lên (id nó đã lấy khi mở thread).
        // Quyền đọc bị giới hạn ở mailbox của chính user (connection "me") nên an toàn.
        return await _gmail.GetAttachmentAsync(
            connection, messageId, attachmentId,
            string.IsNullOrWhiteSpace(filename) ? "attachment" : filename,
            string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType, ct);
    }

    public async Task<byte[]> GetAttachmentsZipAsync(Guid userId, Guid itemId, string messageId, CancellationToken ct = default)
    {
        var (connection, item) = await GetAndValidateConnectionAndItemAsync(userId, null, itemId, ct, requireEditor: false);

        var threadId = GetMetadataString(item.MetadataJson, "threadId");
        if (string.IsNullOrEmpty(threadId)) throw new BusinessRuleException("Item has no threadId in metadata.");

        var thread = await _gmail.GetThreadAsync(connection, threadId, ct);
        var msg = thread.Messages.FirstOrDefault(m => m.MessageId == messageId)
            ?? throw new NotFoundException("Message", messageId);

        if (msg.Attachments.Count == 0)
            throw new BusinessRuleException("Message has no attachments.");

        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var usedNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var attInfo in msg.Attachments)
            {
                var att = await _gmail.GetAttachmentAsync(connection, msg.MessageId, attInfo.AttachmentId, attInfo.Filename, attInfo.MimeType, ct);
                var entryName = UniqueEntryName(string.IsNullOrWhiteSpace(attInfo.Filename) ? "attachment" : attInfo.Filename, usedNames);
                var entry = zip.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Fastest);
                await using var es = entry.Open();
                await es.WriteAsync(att.Data, ct);
            }
        }

        return ms.ToArray();
    }

    /// <summary>Tránh trùng tên file trong zip: "a.pdf" → "a (1).pdf", "a (2).pdf"...</summary>
    private static string UniqueEntryName(string filename, Dictionary<string, int> used)
    {
        if (!used.ContainsKey(filename))
        {
            used[filename] = 0;
            return filename;
        }

        var count = ++used[filename];
        var ext = Path.GetExtension(filename);
        var stem = Path.GetFileNameWithoutExtension(filename);
        var candidate = $"{stem} ({count}){ext}";
        used[candidate] = 0;
        return candidate;
    }

    // ───────────────────────── Private Helpers ─────────────────────────

    /// <summary>Decode danh sách file base64 (client upload) → GmailAttachmentData binary để gắn vào MIME.</summary>
    private static IReadOnlyList<GmailAttachmentData> DecodeAttachments(IEnumerable<AttachmentUpload>? uploads)
    {
        var list = new List<GmailAttachmentData>();
        if (uploads == null) return list;

        foreach (var u in uploads)
        {
            byte[] data;
            try
            {
                data = Convert.FromBase64String(u.ContentBase64);
            }
            catch (FormatException)
            {
                throw new BusinessRuleException($"Attachment '{u.Filename}' has invalid base64 content.");
            }

            var mime = string.IsNullOrWhiteSpace(u.MimeType) ? "application/octet-stream" : u.MimeType;
            list.Add(new GmailAttachmentData(data, u.Filename, mime, data.Length));
        }

        return list;
    }

    private async Task<(Connection connection, Domain.Entities.Item item)> GetAndValidateConnectionAndItemAsync(
        Guid userId, Guid? connectionId, Guid itemId, CancellationToken ct, bool requireEditor = false)
    {
        var item = await _items.GetByIdAsync(itemId, ct)
            ?? throw new NotFoundException("Item", itemId);

        var isOwner = item.UserId == userId;
        var isShared = false;

        if (!isOwner)
        {
            if (requireEditor)
            {
                isShared = await _folders.IsItemSharedWithUserAsEditorAsync(itemId, userId, ct);
            }
            else
            {
                isShared = await _folders.IsItemSharedWithUserAsync(itemId, userId, ct);
            }
        }

        if (!isOwner && !isShared)
            throw new NotFoundException("Item", itemId);
            
        if (item.ConnectionId == null)
            throw new BusinessRuleException("Item has no connection.");

        if (connectionId.HasValue && item.ConnectionId != connectionId)
            throw new BusinessRuleException("Item does not belong to the specified connection.");

        var connection = await _connections.GetByIdTrackedAsync(item.ConnectionId.Value, ct)
            ?? throw new NotFoundException("Connection", item.ConnectionId.Value);

        if (connection.UserId != item.UserId)
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
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse MetadataJson for property '{Property}'", propertyName);
        }
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
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse MetadataJson array for property '{Property}'", propertyName);
        }
        return new List<string>();
    }

    /// <summary>Trích xuất phần email thuần từ chuỗi dạng "Display Name &lt;email@domain.com&gt;" hoặc "email@domain.com".</summary>
    private static string ExtractEmail(string emailOrHeader)
    {
        if (string.IsNullOrWhiteSpace(emailOrHeader)) return emailOrHeader;
        var trimmed = emailOrHeader.Trim();
        var ltIdx = trimmed.LastIndexOf('<');
        var gtIdx = trimmed.LastIndexOf('>');
        if (ltIdx >= 0 && gtIdx > ltIdx)
        {
            return trimmed[(ltIdx + 1)..gtIdx].Trim();
        }
        return trimmed;
    }

    public async Task<ItemResponse> SaveDraftAsync(Guid userId, SaveDraftRequest request, Guid? existingItemId, CancellationToken ct = default)
    {
        var connection = await _connections.GetByIdTrackedAsync(request.ConnectionId, ct)
            ?? throw new NotFoundException("Connection", request.ConnectionId);

        if (connection.UserId != userId)
        {
            var hasAccess = await CheckConnectionAccessAsync(request.ConnectionId, userId, existingItemId, request.ThreadId, ct);
            if (!hasAccess)
                throw new NotFoundException("Connection", request.ConnectionId);
        }

        if (connection.ServiceType != ServiceType.Gmail)
            throw new BusinessRuleException("Only Gmail connections can be used to save drafts.");

        if (connection.Status != ConnectionStatus.Active)
            throw new BusinessRuleException($"Connection is not active (status: {connection.Status}).");

        var attachments = DecodeAttachments(request.Attachments);

        if (existingItemId == null)
        {
            // Create new draft
            var draftResult = await _gmail.CreateDraftAsync(
                connection, request.To, request.Cc, request.Bcc, request.Subject ?? "", request.BodyHtml ?? "", request.ThreadId, request.InReplyToMessageId,
                attachments.Count > 0 ? attachments : null, ct);

            // Construct new local Item
            var metaDict = new Dictionary<string, object>
            {
                { "draftId", draftResult.DraftId },
                { "labels", new List<string> { "DRAFT" } },
                { "threadId", draftResult.ThreadId },
                { "from", connection.ProviderAccountId },
                { "to", request.To },
                { "cc", request.Cc },
                { "bcc", request.Bcc },
                { "subject", request.Subject ?? "" },
                { "bodyHtml", request.BodyHtml ?? "" }
            };
            if (!string.IsNullOrEmpty(request.InReplyToMessageId))
            {
                metaDict["rfc822MessageId"] = request.InReplyToMessageId;
            }

            var item = new Item
            {
                UserId = userId,
                ConnectionId = request.ConnectionId,
                Type = ItemType.Email,
                ExternalId = draftResult.MessageId,
                ThreadId = draftResult.ThreadId,
                Title = string.IsNullOrWhiteSpace(request.Subject) ? "No Subject" : request.Subject,
                Snippet = string.IsNullOrWhiteSpace(request.BodyHtml) ? "" : (request.BodyHtml.Length > 200 ? request.BodyHtml[..200] : request.BodyHtml),
                Status = ItemStatus.Inbox,
                OccurredAt = DateTime.UtcNow,
                MetadataJson = JsonSerializer.Serialize(metaDict)
            };

            await _items.AddAsync(item, ct);
            await _items.SaveChangesAsync(ct);

            var folders = item.ItemFolders?.Select(f => f.FolderId).ToList() ?? new List<Guid>();
            var tags = item.TagAssignments?.Where(ta => ta.Tag != null).Select(ta => new ItemTag(ta.Tag!.Id, ta.Tag.Name, ta.Tag.Color)).ToList() ?? new List<ItemTag>();

            return new ItemResponse(item.Id, item.Type, item.Title, item.Snippet, item.Status, item.OccurredAt, item.DueAt, item.IsImportant, item.ExternalId, item.MetadataJson, folders, tags, item.ConnectionId);
        }
        else
        {
            // Update existing draft
            var item = await _items.GetByIdAsync(existingItemId.Value, ct)
                ?? throw new NotFoundException("Item", existingItemId.Value);

            if (item.UserId != userId)
                throw new NotFoundException("Item", existingItemId.Value);

            if (item.ConnectionId != request.ConnectionId)
                throw new BusinessRuleException("Draft connection mismatch.");

            var draftId = await GetOrResolveDraftIdAsync(connection, item, ct);

            var draftResult = await _gmail.UpdateDraftAsync(
                connection, draftId, request.To, request.Cc, request.Bcc, request.Subject ?? "", request.BodyHtml ?? "", request.ThreadId, request.InReplyToMessageId,
                attachments.Count > 0 ? attachments : null, ct);

            // Update local Item
            var metaDict = new Dictionary<string, object>
            {
                { "draftId", draftResult.DraftId },
                { "labels", new List<string> { "DRAFT" } },
                { "threadId", draftResult.ThreadId },
                { "from", connection.ProviderAccountId },
                { "to", request.To },
                { "cc", request.Cc },
                { "bcc", request.Bcc },
                { "subject", request.Subject ?? "" },
                { "bodyHtml", request.BodyHtml ?? "" }
            };
            if (!string.IsNullOrEmpty(request.InReplyToMessageId))
            {
                metaDict["rfc822MessageId"] = request.InReplyToMessageId;
            }

            item.ExternalId = draftResult.MessageId;
            item.ThreadId = draftResult.ThreadId;
            item.Title = string.IsNullOrWhiteSpace(request.Subject) ? "No Subject" : request.Subject;
            item.Snippet = string.IsNullOrWhiteSpace(request.BodyHtml) ? "" : (request.BodyHtml.Length > 200 ? request.BodyHtml[..200] : request.BodyHtml);
            item.OccurredAt = DateTime.UtcNow;
            item.MetadataJson = JsonSerializer.Serialize(metaDict);

            _items.Update(item);
            await _items.SaveChangesAsync(ct);

            var folders = item.ItemFolders?.Select(f => f.FolderId).ToList() ?? new List<Guid>();
            var tags = item.TagAssignments?.Where(ta => ta.Tag != null).Select(ta => new ItemTag(ta.Tag!.Id, ta.Tag.Name, ta.Tag.Color)).ToList() ?? new List<ItemTag>();

            return new ItemResponse(item.Id, item.Type, item.Title, item.Snippet, item.Status, item.OccurredAt, item.DueAt, item.IsImportant, item.ExternalId, item.MetadataJson, folders, tags, item.ConnectionId);
        }
    }

    public async Task<SendEmailResult> SendDraftAsync(Guid userId, Guid itemId, CancellationToken ct = default)
    {
        var item = await _items.GetByIdAsync(itemId, ct)
            ?? throw new NotFoundException("Item", itemId);

        if (item.UserId != userId)
        {
            var isEditor = await _folders.IsItemSharedWithUserAsEditorAsync(itemId, userId, ct);
            if (!isEditor)
                throw new NotFoundException("Item", itemId);
        }

        if (item.ConnectionId == null)
            throw new BusinessRuleException("Item is not associated with any connection.");

        var connection = await _connections.GetByIdTrackedAsync(item.ConnectionId.Value, ct)
            ?? throw new NotFoundException("Connection", item.ConnectionId.Value);

        if (connection.UserId != item.UserId)
            throw new NotFoundException("Connection", item.ConnectionId.Value);

        if (connection.ServiceType != ServiceType.Gmail)
            throw new BusinessRuleException("Connection is not for Gmail.");

        if (connection.Status != ConnectionStatus.Active)
            throw new BusinessRuleException($"Connection is not active (status: {connection.Status}).");

        var draftId = await GetOrResolveDraftIdAsync(connection, item, ct);

        var sentMessageId = await _gmail.SendDraftAsync(connection, draftId, ct);

        // Update local database Item so it is no longer a draft and moves to sent.
        // We will remove "DRAFT" label and add "SENT" label in metadata.
        var metaDict = string.IsNullOrEmpty(item.MetadataJson) 
            ? new Dictionary<string, object>() 
            : JsonSerializer.Deserialize<Dictionary<string, object>>(item.MetadataJson) ?? new Dictionary<string, object>();

        var labels = new List<string>();
        if (metaDict.TryGetValue("labels", out var lv) && lv is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            labels = je.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0).ToList();
        }
        labels.Remove("DRAFT");
        if (!labels.Contains("SENT"))
        {
            labels.Add("SENT");
        }
        metaDict["labels"] = labels;
        metaDict.Remove("draftId");

        item.ExternalId = sentMessageId;
        item.MetadataJson = JsonSerializer.Serialize(metaDict);
        item.OccurredAt = DateTime.UtcNow;

        _items.Update(item);
        await _items.SaveChangesAsync(ct);

        return new SendEmailResult(sentMessageId, DateTime.UtcNow);
    }

    public async Task DiscardDraftAsync(Guid userId, Guid itemId, CancellationToken ct = default)
    {
        var item = await _items.GetByIdAsync(itemId, ct)
            ?? throw new NotFoundException("Item", itemId);

        if (item.UserId != userId)
        {
            var isEditor = await _folders.IsItemSharedWithUserAsEditorAsync(itemId, userId, ct);
            if (!isEditor)
                throw new NotFoundException("Item", itemId);
        }

        if (item.ConnectionId == null)
            throw new BusinessRuleException("Item is not associated with any connection.");

        var connection = await _connections.GetByIdTrackedAsync(item.ConnectionId.Value, ct)
            ?? throw new NotFoundException("Connection", item.ConnectionId.Value);

        if (connection.UserId != item.UserId)
            throw new NotFoundException("Connection", item.ConnectionId.Value);

        var draftId = await GetOrResolveDraftIdAsync(connection, item, ct);

        if (item.ExternalId != null)
        {
            // Delete the draft message permanently
            await _gmail.DeleteDraftAsync(connection, draftId, ct);
        }

        _items.Remove(item);
        await _items.SaveChangesAsync(ct);
    }

    private async Task<string> GetOrResolveDraftIdAsync(Connection connection, Item item, CancellationToken ct)
    {
        var draftId = GetMetadataString(item.MetadataJson, "draftId");
        if (!string.IsNullOrEmpty(draftId)) return draftId;

        if (string.IsNullOrEmpty(item.ExternalId))
            throw new BusinessRuleException("Item has no message ID to resolve draft ID.");

        var resolvedDraftId = await _gmail.GetDraftIdByMessageIdAsync(connection, item.ExternalId, ct);
        if (string.IsNullOrEmpty(resolvedDraftId))
            throw new BusinessRuleException("Draft could not be resolved on Gmail (it may have been deleted or sent).");

        // Save the resolved draftId back to local metadata
        try
        {
            var metaDict = JsonSerializer.Deserialize<Dictionary<string, object>>(item.MetadataJson ?? "{}") ?? new Dictionary<string, object>();
            metaDict["draftId"] = resolvedDraftId;
            item.MetadataJson = JsonSerializer.Serialize(metaDict);
            _items.Update(item);
            await _items.SaveChangesAsync(ct);
        }
        catch
        {
            // Ignore database save error during resolution, just proceed with the resolved draftId
        }

        return resolvedDraftId;
    }

    private async Task<bool> CheckConnectionAccessAsync(Guid connectionId, Guid userId, Guid? existingItemId, string? threadId, CancellationToken ct)
    {
        var connection = await _connections.GetByIdTrackedAsync(connectionId, ct);
        if (connection == null) return false;

        if (connection.UserId == userId) return true;

        if (existingItemId.HasValue)
        {
            return await _folders.IsItemSharedWithUserAsEditorAsync(existingItemId.Value, userId, ct);
        }

        if (!string.IsNullOrEmpty(threadId))
        {
            var item = await _items.GetByThreadAndConnectionAsync(threadId, connectionId, ct);
            if (item != null)
            {
                return await _folders.IsItemSharedWithUserAsEditorAsync(item.Id, userId, ct);
            }
        }

        return false;
    }
}

