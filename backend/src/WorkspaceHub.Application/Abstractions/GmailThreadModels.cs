namespace WorkspaceHub.Application.Abstractions;

/// <summary>Thông tin 1 attachment trong email (metadata, chưa có data binary).</summary>
public record GmailAttachmentInfo(
    string AttachmentId,
    string Filename,
    string MimeType,
    int Size);

/// <summary>1 message đầy đủ trong thread (có body decoded + attachment metadata).</summary>
public record GmailThreadMessage(
    string MessageId,
    string? From,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string? Subject,
    string? BodyHtml,
    string? BodyPlainText,
    DateTimeOffset? OccurredAt,
    bool IsUnread,
    bool IsStarred,
    bool HasAttachment,
    IReadOnlyList<string> Labels,
    IReadOnlyList<GmailAttachmentInfo> Attachments);

/// <summary>Toàn bộ thread — danh sách messages sắp theo thời gian (cũ → mới).</summary>
public record GmailThread(
    string ThreadId,
    string? Subject,
    IReadOnlyList<GmailThreadMessage> Messages);

/// <summary>Dữ liệu binary của 1 attachment sau khi download từ Gmail.</summary>
public record GmailAttachmentData(
    byte[] Data,
    string Filename,
    string MimeType,
    int Size);
