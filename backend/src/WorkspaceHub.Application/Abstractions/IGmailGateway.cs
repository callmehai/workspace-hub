using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Abstractions;

public interface IGmailGateway
{
    Task<GmailProfile> GetProfileAsync(Connection connection, CancellationToken ct = default);
    Task<GmailMessageList> ListMessageIdsAsync(Connection connection, string? pageToken, int maxResults, CancellationToken ct = default);
    Task<GmailMessage> GetMessageAsync(Connection connection, string messageId, CancellationToken ct = default);
    Task<GmailHistory> ListHistoryAsync(Connection connection, string startHistoryId, string? pageToken, CancellationToken ct = default);
    Task<string?> ModifyMessageAsync(Connection connection, string messageId, IList<string> addLabelIds, IList<string> removeLabelIds, CancellationToken ct = default);
    Task<string?> TrashMessageAsync(Connection connection, string messageId, CancellationToken ct = default);
    Task<string?> UntrashMessageAsync(Connection connection, string messageId, CancellationToken ct = default);
    Task<string?> GetMessageETagAsync(Connection connection, string messageId, CancellationToken ct = default);

    /// <summary>
    /// Gửi 1 email mới qua Gmail (SCRUM-31 — scheduled email). Tự build MIME RFC 2822.
    /// From = connection.ProviderAccountId. Trả về Gmail messageId của thư đã gửi.
    /// Throw ProviderException (502) nếu Google trả lỗi.
    /// </summary>
    Task<string> SendMessageAsync(
        Connection connection,
        IReadOnlyList<string> to,
        IReadOnlyList<string> cc,
        IReadOnlyList<string> bcc,
        string subject,
        string bodyHtml,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy chữ ký (HTML) đã cấu hình trong Gmail cho địa chỉ gửi của connection.
    /// Trả null nếu chưa đặt chữ ký HOẶC connection thiếu scope gmail.settings.basic (không throw).
    /// </summary>
    Task<string?> GetSignatureAsync(Connection connection, CancellationToken ct = default);

    /// <summary>Lấy toàn bộ thread với body decoded + attachment metadata.</summary>
    Task<GmailThread> GetThreadAsync(Connection connection, string threadId, CancellationToken ct = default);

    /// <summary>Download attachment binary data.</summary>
    Task<GmailAttachmentData> GetAttachmentAsync(Connection connection, string messageId, string attachmentId, string filename, string mimeType, CancellationToken ct = default);

    /// <summary>
    /// Gửi reply/forward (email trong thread có sẵn).
    /// threadId để Gmail nhóm, inReplyToMessageId cho header In-Reply-To/References.
    /// attachmentParts cho forward (đính kèm từ email gốc).
    /// </summary>
    Task<string> SendInThreadAsync(
        Connection connection,
        string threadId,
        string? inReplyToMessageId,
        IReadOnlyList<string> to,
        IReadOnlyList<string> cc,
        IReadOnlyList<string> bcc,
        string subject,
        string bodyHtml,
        IReadOnlyList<GmailAttachmentData>? attachments = null,
        CancellationToken ct = default);
}
