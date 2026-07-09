using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// Comment + attachment 2 chiều cho ticket Jira (Item Type=Ticket). Resolve item→connection+issueKey,
/// kiểm ownership (item của user) rồi gọi Jira gateway. Không đụng conflict-guard (comment/attachment độc lập field).
/// </summary>
public interface IJiraTicketService
{
    Task<IReadOnlyList<JiraCommentDto>> GetCommentsAsync(Guid itemId, Guid userId, CancellationToken ct = default);
    Task<JiraCommentDto> AddCommentAsync(Guid itemId, Guid userId, string body, IReadOnlyList<string>? mediaIds = null, CancellationToken ct = default);
    Task<JiraCommentDto> UpdateCommentAsync(Guid itemId, Guid userId, string commentId, string body, CancellationToken ct = default);
    Task DeleteCommentAsync(Guid itemId, Guid userId, string commentId, CancellationToken ct = default);

    Task<IReadOnlyList<JiraAttachmentDto>> GetAttachmentsAsync(Guid itemId, Guid userId, CancellationToken ct = default);
    Task<(byte[] Data, string MimeType, string Filename)> DownloadAttachmentAsync(Guid itemId, Guid userId, string attachmentId, CancellationToken ct = default);
    Task<IReadOnlyList<JiraAttachmentDto>> UploadAttachmentAsync(Guid itemId, Guid userId, string filename, string mimeType, byte[] data, CancellationToken ct = default);
    Task DeleteAttachmentAsync(Guid itemId, Guid userId, string attachmentId, CancellationToken ct = default);
}
