using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Infrastructure.Services;

public class GmailGateway : IGmailGateway
{
    private readonly ITokenService _tokenService;

    public GmailGateway(ITokenService tokenService)
    {
        _tokenService = tokenService;
    }

    private async Task<GmailService> BuildGmailServiceAsync(Connection connection, CancellationToken ct)
    {
        var accessToken = await _tokenService.GetFreshAccessTokenAsync(connection, ct);
        var credential = GoogleCredential.FromAccessToken(accessToken);
        return new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "WorkspaceHub"
        });
    }

    public async Task<GmailProfile> GetProfileAsync(Connection connection, CancellationToken ct = default)
    {
        using var gmail = await BuildGmailServiceAsync(connection, ct);
        var profile = await gmail.Users.GetProfile("me").ExecuteAsync(ct);

        return new GmailProfile(
            profile.EmailAddress,
            profile.HistoryId,
            profile.MessagesTotal,
            profile.ThreadsTotal);
    }

    public async Task<GmailMessageList> ListMessageIdsAsync(Connection connection, string? pageToken, int maxResults, IReadOnlyList<string>? labelIds = null, CancellationToken ct = default)
    {
        using var gmail = await BuildGmailServiceAsync(connection, ct);
        var request = gmail.Users.Messages.List("me");
        request.MaxResults = maxResults;
        if (!string.IsNullOrEmpty(pageToken))
        {
            request.PageToken = pageToken;
        }
        if (labelIds is { Count: > 0 })
        {
            request.LabelIds = labelIds.ToList();
            // messages.list mặc định LOẠI Spam/Trash — phải bật cờ này khi lọc riêng 2 hộp đó.
            if (labelIds.Any(l => l == "SPAM" || l == "TRASH"))
                request.IncludeSpamTrash = true;
        }

        var response = await request.ExecuteAsync(ct);
        var messageIds = response.Messages?.Select(m => m.Id).ToList() ?? new List<string>();

        return new GmailMessageList(messageIds, response.NextPageToken);
    }

    public async Task<GmailMessage> GetMessageAsync(Connection connection, string messageId, CancellationToken ct = default)
    {
        using var gmail = await BuildGmailServiceAsync(connection, ct);
        var request = gmail.Users.Messages.Get("me", messageId);
        
        // FormatEnum.Full lấy đầy đủ cấu trúc payload để kiểm tra attachment chính xác.
        request.Format = Google.Apis.Gmail.v1.UsersResource.MessagesResource.GetRequest.FormatEnum.Full;

        var msg = await request.ExecuteAsync(ct);

        var headers = msg.Payload?.Headers;
        var subject = DecodeMimeHeader(headers?.FirstOrDefault(h => h.Name.Equals("Subject", StringComparison.OrdinalIgnoreCase))?.Value);
        var from = DecodeMimeHeader(headers?.FirstOrDefault(h => h.Name.Equals("From", StringComparison.OrdinalIgnoreCase))?.Value);
        var toHeader = headers?.FirstOrDefault(h => h.Name.Equals("To", StringComparison.OrdinalIgnoreCase))?.Value;

        var toList = string.IsNullOrEmpty(toHeader)
            ? (IReadOnlyList<string>)new List<string>()
            : toHeader.Split(',').Select(x => DecodeMimeHeader(x.Trim())!).ToList();

        var ccHeader = headers?.FirstOrDefault(h => h.Name.Equals("Cc", StringComparison.OrdinalIgnoreCase))?.Value;
        var ccList = string.IsNullOrEmpty(ccHeader)
            ? (IReadOnlyList<string>)new List<string>()
            : ccHeader.Split(',').Select(x => DecodeMimeHeader(x.Trim())!).ToList();

        var bccHeader = headers?.FirstOrDefault(h => h.Name.Equals("Bcc", StringComparison.OrdinalIgnoreCase))?.Value;
        var bccList = string.IsNullOrEmpty(bccHeader)
            ? (IReadOnlyList<string>)new List<string>()
            : bccHeader.Split(',').Select(x => DecodeMimeHeader(x.Trim())!).ToList();

        var rfc822MessageId = headers?.FirstOrDefault(h => h.Name.Equals("Message-ID", StringComparison.OrdinalIgnoreCase))?.Value;

        bool hasAttachment = msg.Payload != null && CheckHasAttachment(msg.Payload);

        // Extract body HTML/plain for forwarding
        string? bodyHtml = null;
        string? bodyPlain = null;
        var dummyAtts = new List<GmailAttachmentInfo>();
        if (msg.Payload != null)
        {
            ExtractBodyAndAttachments(msg.Payload, ref bodyHtml, ref bodyPlain, dummyAtts);
        }

        DateTimeOffset? occurredAt = null;
        if (msg.InternalDate.HasValue)
        {
            occurredAt = DateTimeOffset.FromUnixTimeMilliseconds(msg.InternalDate.Value);
        }

        return new GmailMessage(
            msg.Id,
            msg.ThreadId,
            subject,
            from,
            toList,
            ccList,
            bccList,
            msg.Snippet,
            msg.LabelIds?.ToList() ?? new List<string>(),
            hasAttachment,
            occurredAt,
            msg.HistoryId?.ToString(),
            rfc822MessageId,
            bodyHtml,
            bodyPlain);
    }

    public async Task<GmailHistory> ListHistoryAsync(Connection connection, string startHistoryId, string? pageToken, CancellationToken ct = default)
    {
        if (!ulong.TryParse(startHistoryId, out ulong historyId))
        {
            return new GmailHistory(true, new List<string>(), null, null);
        }

        using var gmail = await BuildGmailServiceAsync(connection, ct);
        var request = gmail.Users.History.List("me");
        request.StartHistoryId = historyId;
        if (!string.IsNullOrEmpty(pageToken))
        {
            request.PageToken = pageToken;
        }

        try
        {
            var response = await request.ExecuteAsync(ct);
            var affectedIds = response.History?
                .SelectMany(h => 
                {
                    var ids = new List<string>();
                    if (h.MessagesAdded != null) ids.AddRange(h.MessagesAdded.Where(m => m.Message?.Id != null).Select(m => m.Message.Id));
                    if (h.LabelsAdded != null) ids.AddRange(h.LabelsAdded.Where(m => m.Message?.Id != null).Select(m => m.Message.Id));
                    if (h.LabelsRemoved != null) ids.AddRange(h.LabelsRemoved.Where(m => m.Message?.Id != null).Select(m => m.Message.Id));
                    return ids;
                })
                .Where(id => id != null)
                .Distinct()
                .ToList() ?? new List<string>();

            return new GmailHistory(
                false,
                affectedIds,
                response.NextPageToken,
                response.HistoryId?.ToString());
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new GmailHistory(true, new List<string>(), null, null);
        }
    }

    public async Task<string?> ModifyMessageAsync(Connection connection, string messageId, IList<string> addLabelIds, IList<string> removeLabelIds, CancellationToken ct = default)
    {
        try
        {
            using var gmail = await BuildGmailServiceAsync(connection, ct);
            var req = new Google.Apis.Gmail.v1.Data.ModifyMessageRequest
            {
                AddLabelIds = addLabelIds,
                RemoveLabelIds = removeLabelIds
            };
            await gmail.Users.Messages.Modify(req, "me", messageId).ExecuteAsync(ct);
            return await GetMessageETagAsync(connection, messageId, ct);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Gmail", "Message", messageId);
        }
    }

    public async Task<string?> TrashMessageAsync(Connection connection, string messageId, CancellationToken ct = default)
    {
        try
        {
            using var gmail = await BuildGmailServiceAsync(connection, ct);
            await gmail.Users.Messages.Trash("me", messageId).ExecuteAsync(ct);
            return await GetMessageETagAsync(connection, messageId, ct);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Gmail", "Message", messageId);
        }
    }

    public async Task TrashThreadAsync(Connection connection, string threadId, CancellationToken ct = default)
    {
        try
        {
            using var gmail = await BuildGmailServiceAsync(connection, ct);
            await gmail.Users.Threads.Trash("me", threadId).ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Gmail", "Thread", threadId);
        }
    }

    public async Task<string?> UntrashMessageAsync(Connection connection, string messageId, CancellationToken ct = default)
    {
        try
        {
            using var gmail = await BuildGmailServiceAsync(connection, ct);
            await gmail.Users.Messages.Untrash("me", messageId).ExecuteAsync(ct);
            return await GetMessageETagAsync(connection, messageId, ct);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Gmail", "Message", messageId);
        }
    }

    public async Task<string?> GetMessageETagAsync(Connection connection, string messageId, CancellationToken ct = default)
    {
        try
        {
            using var gmail = await BuildGmailServiceAsync(connection, ct);
            var request = gmail.Users.Messages.Get("me", messageId);
            request.Format = Google.Apis.Gmail.v1.UsersResource.MessagesResource.GetRequest.FormatEnum.Minimal;
            var msg = await request.ExecuteAsync(ct);
            return msg.HistoryId?.ToString();
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Gmail", "Message", messageId);
        }
    }

    public async Task<string> SendMessageAsync(
        Connection connection,
        IReadOnlyList<string> to,
        IReadOnlyList<string> cc,
        IReadOnlyList<string> bcc,
        string subject,
        string bodyHtml,
        IReadOnlyList<GmailAttachmentData>? attachments = null,
        CancellationToken ct = default)
    {
        try
        {
            using var gmail = await BuildGmailServiceAsync(connection, ct);

            var raw = BuildMimeMessage(connection.ProviderAccountId, to, cc, bcc, subject, bodyHtml, null, attachments);
            var message = new Google.Apis.Gmail.v1.Data.Message { Raw = raw };

            var sent = await gmail.Users.Messages.Send(message, "me").ExecuteAsync(ct);
            return sent.Id;
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Gmail", "Message", "send");
        }
    }

    public async Task<string?> GetSignatureAsync(Connection connection, CancellationToken ct = default)
    {
        try
        {
            using var gmail = await BuildGmailServiceAsync(connection, ct);
            var list = await gmail.Users.Settings.SendAs.List("me").ExecuteAsync(ct);

            // Ưu tiên địa chỉ primary; fallback theo email của connection.
            var sendAs = list.SendAs?.FirstOrDefault(s => s.IsPrimary == true)
                ?? list.SendAs?.FirstOrDefault(s =>
                    string.Equals(s.SendAsEmail, connection.ProviderAccountId, StringComparison.OrdinalIgnoreCase));

            return string.IsNullOrWhiteSpace(sendAs?.Signature) ? null : sendAs.Signature;
        }
        catch (Google.GoogleApiException)
        {
            // Thiếu scope gmail.settings.basic (connection cũ) hoặc lỗi khác → coi như không có chữ ký.
            return null;
        }
    }

    public async Task<GmailThread> GetThreadAsync(Connection connection, string threadId, CancellationToken ct = default)
    {
        try
        {
            using var gmail = await BuildGmailServiceAsync(connection, ct);
            var req = gmail.Users.Threads.Get("me", threadId);
            req.Format = Google.Apis.Gmail.v1.UsersResource.ThreadsResource.GetRequest.FormatEnum.Full;
            var thread = await req.ExecuteAsync(ct);

            var messages = new List<GmailThreadMessage>();
            string? threadSubject = null;

            if (thread.Messages != null)
            {
                foreach (var msg in thread.Messages)
                {
                    var headers = msg.Payload?.Headers;
                    var subject = DecodeMimeHeader(headers?.FirstOrDefault(h => h.Name.Equals("Subject", StringComparison.OrdinalIgnoreCase))?.Value);
                    if (threadSubject == null && !string.IsNullOrEmpty(subject)) threadSubject = subject;

                    var from = DecodeMimeHeader(headers?.FirstOrDefault(h => h.Name.Equals("From", StringComparison.OrdinalIgnoreCase))?.Value);
                    var toHeader = headers?.FirstOrDefault(h => h.Name.Equals("To", StringComparison.OrdinalIgnoreCase))?.Value;
                    var ccHeader = headers?.FirstOrDefault(h => h.Name.Equals("Cc", StringComparison.OrdinalIgnoreCase))?.Value;
                    var bccHeader = headers?.FirstOrDefault(h => h.Name.Equals("Bcc", StringComparison.OrdinalIgnoreCase))?.Value;

                    var toList = string.IsNullOrEmpty(toHeader) ? new List<string>() : toHeader.Split(',').Select(x => DecodeMimeHeader(x.Trim())!).ToList();
                    var ccList = string.IsNullOrEmpty(ccHeader) ? new List<string>() : ccHeader.Split(',').Select(x => DecodeMimeHeader(x.Trim())!).ToList();
                    var bccList = string.IsNullOrEmpty(bccHeader) ? new List<string>() : bccHeader.Split(',').Select(x => DecodeMimeHeader(x.Trim())!).ToList();

                    string? html = null;
                    string? plain = null;
                    var atts = new List<GmailAttachmentInfo>();
                    ExtractBodyAndAttachments(msg.Payload, ref html, ref plain, atts);

                    DateTimeOffset? occurredAt = msg.InternalDate.HasValue 
                        ? DateTimeOffset.FromUnixTimeMilliseconds(msg.InternalDate.Value) 
                        : null;
                    
                    var labels = msg.LabelIds?.ToList() ?? new List<string>();
                    bool isUnread = labels.Contains("UNREAD");
                    bool isStarred = labels.Contains("STARRED");

                    messages.Add(new GmailThreadMessage(
                        msg.Id, from, toList, ccList, bccList, subject, html, plain, occurredAt, 
                        isUnread, isStarred, atts.Count > 0, labels, atts));
                }
            }
            return new GmailThread(threadId, threadSubject, messages);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Gmail", "Thread", threadId);
        }
    }

    public async Task<GmailAttachmentData> GetAttachmentAsync(Connection connection, string messageId, string attachmentId, string filename, string mimeType, CancellationToken ct = default)
    {
        try
        {
            using var gmail = await BuildGmailServiceAsync(connection, ct);
            var req = gmail.Users.Messages.Attachments.Get("me", messageId, attachmentId);
            var att = await req.ExecuteAsync(ct);
            
            var base64 = att.Data.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            var data = Convert.FromBase64String(base64);
            return new GmailAttachmentData(data, filename, mimeType, att.Size ?? data.Length);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Gmail", "Attachment", attachmentId);
        }
    }

    public async Task<string> SendInThreadAsync(
        Connection connection,
        string threadId,
        string? inReplyToMessageId,
        IReadOnlyList<string> to,
        IReadOnlyList<string> cc,
        IReadOnlyList<string> bcc,
        string subject,
        string bodyHtml,
        IReadOnlyList<GmailAttachmentData>? attachments = null,
        CancellationToken ct = default)
    {
        try
        {
            using var gmail = await BuildGmailServiceAsync(connection, ct);
            var raw = BuildMimeMessage(connection.ProviderAccountId, to, cc, bcc, subject, bodyHtml, inReplyToMessageId, attachments);
            var message = new Google.Apis.Gmail.v1.Data.Message 
            { 
                Raw = raw, 
                ThreadId = threadId 
            };
            var sent = await gmail.Users.Messages.Send(message, "me").ExecuteAsync(ct);
            return sent.Id;
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Gmail", "Message", "send_in_thread");
        }
    }

    // ───────────────────────── Private helpers ─────────────────────────

    /// <summary>
    /// Build MIME RFC 2822 rồi base64url-encode cho field Message.Raw.
    /// Subject mã hoá theo encoded-word UTF-8 để giữ Unicode; body là text/html UTF-8.
    /// </summary>
    private static string BuildMimeMessage(
        string from,
        IReadOnlyList<string> to,
        IReadOnlyList<string> cc,
        IReadOnlyList<string> bcc,
        string subject,
        string bodyHtml,
        string? inReplyToMessageId = null,
        IReadOnlyList<GmailAttachmentData>? attachments = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("From: ").Append(EncodeAddress(from)).Append("\r\n");
        sb.Append("To: ").Append(EncodeAddressList(to)).Append("\r\n");
        if (cc.Count > 0)
        {
            sb.Append("Cc: ").Append(EncodeAddressList(cc)).Append("\r\n");
        }
        if (bcc.Count > 0)
        {
            sb.Append("Bcc: ").Append(EncodeAddressList(bcc)).Append("\r\n");
        }
        sb.Append("Subject: ").Append(EncodeHeaderValue(subject)).Append("\r\n");
        
        if (!string.IsNullOrEmpty(inReplyToMessageId))
        {
            var id = inReplyToMessageId.StartsWith("<") ? inReplyToMessageId : $"<{inReplyToMessageId}>";
            sb.Append("In-Reply-To: ").Append(id).Append("\r\n");
            sb.Append("References: ").Append(id).Append("\r\n");
        }

        sb.Append("MIME-Version: 1.0\r\n");

        if (attachments != null && attachments.Count > 0)
        {
            string boundary = "----=_Part_" + Guid.NewGuid().ToString("N");
            sb.Append($"Content-Type: multipart/mixed; boundary=\"{boundary}\"\r\n\r\n");
            
            sb.Append($"--{boundary}\r\n");
            sb.Append("Content-Type: text/html; charset=\"UTF-8\"\r\n");
            sb.Append("Content-Transfer-Encoding: base64\r\n\r\n");
            sb.Append(Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(bodyHtml),
                Base64FormattingOptions.InsertLineBreaks)).Append("\r\n\r\n");

            foreach (var att in attachments)
            {
                sb.Append($"--{boundary}\r\n");
                // Tên file có thể chứa tiếng Việt, cần encode
                string encodedName = EncodeHeaderValue(att.Filename);
                // Với MIME headers, nếu encode =?UTF-8?B?... thì không cần ngoặc kép, nhưng ngoặc kép vẫn an toàn.
                sb.Append($"Content-Type: {att.MimeType}; name=\"{encodedName}\"\r\n");
                sb.Append($"Content-Disposition: attachment; filename=\"{encodedName}\"\r\n");
                sb.Append("Content-Transfer-Encoding: base64\r\n\r\n");
                sb.Append(Convert.ToBase64String(att.Data, Base64FormattingOptions.InsertLineBreaks)).Append("\r\n\r\n");
            }
            sb.Append($"--{boundary}--\r\n");
        }
        else
        {
            sb.Append("Content-Type: text/html; charset=\"UTF-8\"\r\n");
            sb.Append("Content-Transfer-Encoding: base64\r\n");
            sb.Append("\r\n");
            // Wrap base64 mỗi 76 ký tự (RFC 2045) — tránh 1 dòng dài vượt giới hạn 998 octet của SMTP (RFC 5321) với body HTML nhiều KB.
            sb.Append(Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(bodyHtml),
                Base64FormattingOptions.InsertLineBreaks));
        }

        var rawBytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return Base64UrlEncode(rawBytes);
    }

    /// <summary>
    /// Encoded-word (RFC 2047) cho header chứa Unicode — vd Subject tiếng Việt.
    /// Fold thành nhiều encoded-word ≤ 75 ký tự (RFC 2047 §2), tách bằng CRLF + space,
    /// để Subject dài không tạo 1 dòng header vượt 998 octet (RFC 5321). Cắt theo ranh
    /// giới code point (Rune) nên không bao giờ tách đôi ký tự multi-byte.
    /// </summary>
    private static string EncodeHeaderValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        const int maxBytesPerWord = 45; // base64(45B)=60 ký tự + "=?UTF-8?B??=" (12) = 72 ≤ 75.
        var words = new List<string>();
        var chunk = new List<byte>(maxBytesPerWord + 4);
        Span<byte> buf = stackalloc byte[4];

        foreach (var rune in value.EnumerateRunes())
        {
            var n = rune.EncodeToUtf8(buf);
            if (chunk.Count > 0 && chunk.Count + n > maxBytesPerWord)
            {
                words.Add(ToEncodedWord(chunk));
                chunk.Clear();
            }
            for (var i = 0; i < n; i++) chunk.Add(buf[i]);
        }
        if (chunk.Count > 0) words.Add(ToEncodedWord(chunk));

        return string.Join("\r\n ", words);
    }

    private static string ToEncodedWord(List<byte> bytes)
        => $"=?UTF-8?B?{Convert.ToBase64String(bytes.ToArray())}?=";

    /// <summary>Mã hoá header địa chỉ (To/Cc/Bcc): encode phần display-name non-ASCII (RFC 2047), giữ nguyên &lt;email&gt;.</summary>
    private static string EncodeAddressList(IReadOnlyList<string> addresses)
        => string.Join(", ", addresses.Select(EncodeAddress));

    /// <summary>Encode 1 địa chỉ "Display Name &lt;email&gt;" — encode display-name nếu chứa ký tự non-ASCII (vd tên tiếng Việt); email thuần ASCII giữ nguyên.</summary>
    private static string EncodeAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return address;
        var s = address.Trim();

        var lt = s.LastIndexOf('<');
        var gt = s.LastIndexOf('>');
        if (lt > 0 && gt > lt)
        {
            var display = s[..lt].Trim().Trim('"').Trim();
            var email = s[lt..]; // "<email@domain>"
            if (string.IsNullOrEmpty(display) || IsAscii(display)) return s;
            return $"{EncodeHeaderValue(display)} {email}";
        }
        return s; // chỉ có email thuần
    }

    private static bool IsAscii(string s)
    {
        foreach (var c in s) if (c > 127) return false;
        return true;
    }

    /// <summary>
    /// Giải mã encoded-word RFC 2047 (=?charset?B|Q?text?=) trong header đọc từ Gmail
    /// (vd display-name tiếng Việt). Trả nguyên văn nếu không chứa encoded-word.
    /// </summary>
    private static string? DecodeMimeHeader(string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains("=?")) return value;

        // Encoded-word liền nhau cách nhau chỉ bằng whitespace phải nối liền (RFC 2047 §6.2).
        var collapsed = System.Text.RegularExpressions.Regex.Replace(
            value, @"(=\?[^?]+\?[BbQq]\?[^?]*\?=)\s+(?==\?)", "$1");

        return System.Text.RegularExpressions.Regex.Replace(
            collapsed, @"=\?([^?]+)\?([BbQq])\?([^?]*)\?=", m =>
            {
                try
                {
                    var charset = m.Groups[1].Value;
                    var enc = char.ToUpperInvariant(m.Groups[2].Value[0]);
                    var text = m.Groups[3].Value;
                    var bytes = enc == 'B' ? Convert.FromBase64String(text) : DecodeQ(text);
                    return System.Text.Encoding.GetEncoding(charset).GetString(bytes);
                }
                catch { return m.Value; }
            });
    }

    /// <summary>Giải mã Q-encoding (RFC 2047): '_' → space, '=XX' → byte hex.</summary>
    private static byte[] DecodeQ(string text)
    {
        var bytes = new List<byte>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '_') bytes.Add((byte)' ');
            else if (c == '=' && i + 2 < text.Length &&
                     byte.TryParse(text.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                bytes.Add(b);
                i += 2;
            }
            else bytes.Add((byte)c);
        }
        return bytes.ToArray();
    }

    /// <summary>Base64url (RFC 4648) — '+'→'-', '/'→'_', bỏ '=' padding theo yêu cầu Gmail API.</summary>
    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    /// <summary>Đệ quy tìm xem có Part nào chứa attachment (có filename).</summary>
    private static bool CheckHasAttachment(Google.Apis.Gmail.v1.Data.MessagePart part)
    {
        if (!string.IsNullOrEmpty(part.Filename)) return true;
        if (part.Parts != null)
        {
            foreach (var child in part.Parts)
            {
                if (CheckHasAttachment(child)) return true;
            }
        }
        return false;
    }

    private static void ExtractBodyAndAttachments(
        Google.Apis.Gmail.v1.Data.MessagePart? part, 
        ref string? html, 
        ref string? plain, 
        List<GmailAttachmentInfo> attachments)
    {
        if (part == null) return;
        
        if (!string.IsNullOrEmpty(part.Filename) && part.Body?.AttachmentId != null)
        {
            attachments.Add(new GmailAttachmentInfo(
                part.Body.AttachmentId, 
                part.Filename, 
                part.MimeType ?? "application/octet-stream", 
                part.Body.Size ?? 0));
        }
        else if (part.MimeType == "text/html" && part.Body?.Data != null)
        {
            if (html == null) html = Base64UrlDecodeString(part.Body.Data);
        }
        else if (part.MimeType == "text/plain" && part.Body?.Data != null)
        {
            if (plain == null) plain = Base64UrlDecodeString(part.Body.Data);
        }

        if (part.Parts != null)
        {
            foreach (var child in part.Parts)
            {
                ExtractBodyAndAttachments(child, ref html, ref plain, attachments);
            }
        }
    }

    private static string Base64UrlDecodeString(string data)
    {
        var base64 = data.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
}
