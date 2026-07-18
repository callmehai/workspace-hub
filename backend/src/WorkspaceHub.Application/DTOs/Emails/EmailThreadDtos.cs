namespace WorkspaceHub.Application.DTOs.Emails;

// ───────────────────────── Thread Response ─────────────────────────

/// <summary>Toàn bộ luồng hội thoại email — list messages sắp theo thời gian (cũ → mới).</summary>
public record EmailThreadResponse(
    string ThreadId,
    string? Subject,
    IReadOnlyList<EmailThreadMessageDto> Messages,
    /// <summary>
    /// Email của hộp thư chứa thread này (= chủ sở hữu item). FE dùng làm "tôi là ai" khi dựng
    /// danh sách người nhận lúc Reply/Reply-All. Với người được chia sẻ folder, họ KHÔNG sở hữu
    /// connection này nên không tự tra ra được — phải lấy từ đây.
    /// </summary>
    string? OwnerEmail = null);

/// <summary>1 message trong thread — có body decoded + attachment metadata.</summary>
public record EmailThreadMessageDto(
    string MessageId,
    string? From,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string? Subject,
    string? BodyHtml,
    string? BodyPlainText,
    DateTime OccurredAt,
    bool IsUnread,
    bool IsStarred,
    bool HasAttachment,
    IReadOnlyList<string> Labels,
    IReadOnlyList<EmailAttachmentDto> Attachments,
    Guid? ItemId = null);

/// <summary>Thông tin 1 attachment (metadata, không có data binary).</summary>
public record EmailAttachmentDto(
    string AttachmentId,
    string Filename,
    string MimeType,
    int Size);
