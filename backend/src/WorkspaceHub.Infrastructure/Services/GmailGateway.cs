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

    public async Task<GmailMessageList> ListMessageIdsAsync(Connection connection, string? pageToken, int maxResults, CancellationToken ct = default)
    {
        using var gmail = await BuildGmailServiceAsync(connection, ct);
        var request = gmail.Users.Messages.List("me");
        request.MaxResults = maxResults;
        if (!string.IsNullOrEmpty(pageToken))
        {
            request.PageToken = pageToken;
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
        var subject = headers?.FirstOrDefault(h => h.Name.Equals("Subject", StringComparison.OrdinalIgnoreCase))?.Value;
        var from = headers?.FirstOrDefault(h => h.Name.Equals("From", StringComparison.OrdinalIgnoreCase))?.Value;
        var toHeader = headers?.FirstOrDefault(h => h.Name.Equals("To", StringComparison.OrdinalIgnoreCase))?.Value;
        
        var toList = string.IsNullOrEmpty(toHeader) 
            ? (IReadOnlyList<string>)new List<string>() 
            : toHeader.Split(',').Select(x => x.Trim()).ToList();

        bool hasAttachment = msg.Payload != null && CheckHasAttachment(msg.Payload);

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
            msg.Snippet,
            msg.LabelIds?.ToList() ?? new List<string>(),
            hasAttachment,
            occurredAt,
            msg.HistoryId?.ToString());
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
            var addedIds = response.History?
                .Where(h => h.MessagesAdded != null)
                .SelectMany(h => h.MessagesAdded)
                .Where(m => m.Message?.Id != null)
                .Select(m => m.Message.Id)
                .Distinct()
                .ToList() ?? new List<string>();

            return new GmailHistory(
                false,
                addedIds,
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
            var response = await gmail.Users.Messages.Modify(req, "me", messageId).ExecuteAsync(ct);
            return response.HistoryId?.ToString();
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
            var response = await gmail.Users.Messages.Trash("me", messageId).ExecuteAsync(ct);
            return response.HistoryId?.ToString();
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Gmail", "Message", messageId);
        }
    }

    public async Task<string?> UntrashMessageAsync(Connection connection, string messageId, CancellationToken ct = default)
    {
        try
        {
            using var gmail = await BuildGmailServiceAsync(connection, ct);
            var response = await gmail.Users.Messages.Untrash("me", messageId).ExecuteAsync(ct);
            return response.HistoryId?.ToString();
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
        CancellationToken ct = default)
    {
        try
        {
            using var gmail = await BuildGmailServiceAsync(connection, ct);

            var raw = BuildMimeMessage(connection.ProviderAccountId, to, cc, bcc, subject, bodyHtml);
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
        string bodyHtml)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("From: ").Append(from).Append("\r\n");
        sb.Append("To: ").Append(string.Join(", ", to)).Append("\r\n");
        if (cc.Count > 0)
        {
            sb.Append("Cc: ").Append(string.Join(", ", cc)).Append("\r\n");
        }
        if (bcc.Count > 0)
        {
            sb.Append("Bcc: ").Append(string.Join(", ", bcc)).Append("\r\n");
        }
        sb.Append("Subject: ").Append(EncodeHeaderValue(subject)).Append("\r\n");
        sb.Append("MIME-Version: 1.0\r\n");
        sb.Append("Content-Type: text/html; charset=\"UTF-8\"\r\n");
        sb.Append("Content-Transfer-Encoding: base64\r\n");
        sb.Append("\r\n");
        // Wrap base64 mỗi 76 ký tự (RFC 2045) — tránh 1 dòng dài vượt giới hạn 998 octet của SMTP (RFC 5321) với body HTML nhiều KB.
        sb.Append(Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(bodyHtml),
            Base64FormattingOptions.InsertLineBreaks));

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
}
