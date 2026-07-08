using WorkspaceHub.Application.DTOs.Emails;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>Gửi email trực tiếp (gửi ngay) qua Gmail — Gmail write-back "gửi mới".</summary>
public interface ISendEmailService
{
    Task<SendEmailResult> SendAsync(Guid userId, SendEmailRequest request, CancellationToken ct = default);

    /// <summary>Lấy chữ ký Gmail của connection (null nếu chưa đặt / thiếu scope). Validate connection thuộc user + Gmail.</summary>
    Task<string?> GetSignatureAsync(Guid userId, Guid connectionId, CancellationToken ct = default);

    /// <summary>Danh sách contact cache theo connection (Contact + OtherContact) — OData filter/sort/paging ở controller.</summary>
    Task<IReadOnlyList<ContactSuggestionDto>> GetContactSuggestionsAsync(
        Guid userId, Guid connectionId, CancellationToken ct = default);
}
