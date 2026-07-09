namespace WorkspaceHub.Application.DTOs.Emails;

// ───────────────────────── Thread Response ─────────────────────────

/// <summary>Toàn bộ luồng hội thoại email — list messages sắp theo thời gian (cũ → mới).</summary>
public record EmailThreadResponse(
    string ThreadId,
    string? Subject,
    IReadOnlyList<EmailThreadMessageDto> Messages);

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
    IReadOnlyList<EmailAttachmentDto> Attachments);

/// <summary>Thông tin 1 attachment (metadata, không có data binary).</summary>
public record EmailAttachmentDto(
    string AttachmentId,
    string Filename,
    string MimeType,
    int Size);
