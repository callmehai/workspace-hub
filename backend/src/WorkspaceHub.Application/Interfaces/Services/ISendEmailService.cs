using WorkspaceHub.Application.DTOs.Emails;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>Gửi email trực tiếp (gửi ngay) qua Gmail — Gmail write-back "gửi mới".</summary>
public interface ISendEmailService
{
    Task<SendEmailResult> SendAsync(Guid userId, SendEmailRequest request, CancellationToken ct = default);

    /// <summary>Lấy chữ ký Gmail của connection (null nếu chưa đặt / thiếu scope). Validate connection thuộc user + Gmail.</summary>
    Task<string?> GetSignatureAsync(Guid userId, Guid connectionId, CancellationToken ct = default);

    /// <summary>OData list — validate connection Gmail rồi trả IQueryable in-memory.</summary>
    Task<IQueryable<ContactSuggestionDto>> GetContactSuggestionsAsync(
        Guid userId, Guid connectionId, CancellationToken ct = default);

    Task<EmailThreadResponse> GetThreadAsync(
        Guid userId, Guid itemId, CancellationToken ct = default);

    Task<SendInThreadResult> ReplyAsync(
        Guid userId, ReplyEmailRequest request, CancellationToken ct = default);

    Task<SendInThreadResult> ForwardAsync(
        Guid userId, ForwardEmailRequest request, CancellationToken ct = default);

    Task<Application.Abstractions.GmailAttachmentData> GetAttachmentAsync(
        Guid userId, Guid itemId, string attachmentId, CancellationToken ct = default);
}
