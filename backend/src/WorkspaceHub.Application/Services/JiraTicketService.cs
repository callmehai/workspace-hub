using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

public class JiraTicketService : IJiraTicketService
{
    private readonly IItemRepository _items;
    private readonly IConnectionRepository _connections;
    private readonly IJiraGateway _gateway;
    private readonly IFolderRepository _folders;

    public JiraTicketService(IItemRepository items, IConnectionRepository connections, IJiraGateway gateway,
        IFolderRepository folders)
    {
        _items = items;
        _connections = connections;
        _gateway = gateway;
        _folders = folders;
    }

    // ── Resolve item→(connection, issueKey) + kiểm ownership + Jira active ──
    private async Task<(Connection Conn, string Key)> ResolveAsync(Guid itemId, Guid userId, CancellationToken ct)
    {
        // Owner HOẶC shared-Editor (mirror ItemWriteBackService) — trước đây chỉ check owner
        // nên Editor của folder chứa ticket bị 404 ở mọi thao tác comment/attachment.
        var item = await _items.GetByIdAndUserAsync(itemId, userId, ct);
        if (item == null && await _folders.IsItemSharedWithUserAsEditorAsync(itemId, userId, ct))
            item = await _items.GetByIdAsync(itemId, ct);
        if (item == null)
        {
            // Viewer của folder chia sẻ cố comment/đính kèm → 403 kèm giải thích, thay vì
            // 404 "Item with id '...' was not found" (lộ GUID, người dùng không hiểu vì sao).
            if (await _folders.IsItemSharedWithUserAsync(itemId, userId, ct))
                throw new ForbiddenException(
                    "You only have view access to this item in a shared folder.", ErrorCodes.SharedViewerReadOnly);

            throw new NotFoundException("Item", itemId);
        }

        if (item.Type != ItemType.Ticket) throw new BusinessRuleException("Item không phải ticket Jira.");
        if (item.ExternalId == null) throw new BusinessRuleException("Ticket thiếu issue key.");
        if (item.ConnectionId == null) throw new BusinessRuleException("Ticket không gắn connection.");

        var conn = await _connections.GetByIdAsync(item.ConnectionId.Value, ct)
            ?? throw new NotFoundException("Connection", item.ConnectionId.Value);
        if (conn.ServiceType != ServiceType.Jira) throw new BusinessRuleException("Connection không phải Jira.");
        if (conn.Status != ConnectionStatus.Active) throw new BusinessRuleException("Kết nối Jira không hoạt động — cần kết nối lại.");

        return (conn, item.ExternalId);
    }

    private static JiraCommentDto ToDto(JiraComment c) =>
        new(c.Id, c.BodyText, c.AuthorName, c.AuthorAccountId, c.Created, c.Updated);

    private static JiraAttachmentDto ToDto(JiraAttachment a) =>
        new(a.Id, a.Filename, a.MimeType, a.Size, a.AuthorName, a.Created);

    // ── Comments ──
    public async Task<IReadOnlyList<JiraCommentDto>> GetCommentsAsync(Guid itemId, Guid userId, CancellationToken ct = default)
    {
        var (conn, key) = await ResolveAsync(itemId, userId, ct);
        var comments = await _gateway.GetCommentsAsync(conn, key, ct);
        return comments.Select(ToDto).ToList();
    }

    public async Task<JiraCommentDto> AddCommentAsync(Guid itemId, Guid userId, string body, IReadOnlyList<string>? mediaIds = null, CancellationToken ct = default)
    {
        // Cho phép body rỗng NẾU có media đính kèm (comment chỉ có file).
        var hasMedia = mediaIds != null && mediaIds.Count > 0;
        if (string.IsNullOrWhiteSpace(body) && !hasMedia) throw new BusinessRuleException("Nội dung comment không được rỗng.");
        var (conn, key) = await ResolveAsync(itemId, userId, ct);

        var finalBody = body?.Trim() ?? string.Empty;
        if (hasMedia)
        {
            // Nhúng attachment vào comment dạng LINK markdown [📎 tên](url) — Jira hiện link bấm được,
            // app mình nhận diện URL .../attachment/content/{id} để render chip/preview.
            // href phải là URL SITE (xxx.atlassian.net) — KHÔNG dùng field `content` (api.atlassian.com cần OAuth,
            // bấm trong browser sẽ báo "không có quyền"). Site URL bấm được bằng session Jira của user.
            var atts = await _gateway.GetAttachmentsAsync(conn, key, ct);
            var siteUrl = (await _gateway.GetSiteUrlAsync(conn, ct))?.TrimEnd('/');
            var links = mediaIds!
                .Select(id => atts.FirstOrDefault(a => a.Id == id))
                .Where(a => a != null)
                .Select(a =>
                {
                    var href = siteUrl != null
                        ? $"{siteUrl}/rest/api/3/attachment/content/{a!.Id}"
                        : a!.ContentUrl;
                    return $"[📎 {a!.Filename}]({href})";
                })
                .Where(l => !l.Contains("]()"))
                .ToList();
            if (links.Count > 0)
                finalBody = finalBody.Length == 0 ? string.Join("\n", links) : finalBody + "\n" + string.Join("\n", links);
        }

        return ToDto(await _gateway.AddCommentAsync(conn, key, finalBody, null, ct));
    }

    public async Task<JiraCommentDto> UpdateCommentAsync(Guid itemId, Guid userId, string commentId, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body)) throw new BusinessRuleException("Nội dung comment không được rỗng.");
        var (conn, key) = await ResolveAsync(itemId, userId, ct);
        return ToDto(await _gateway.UpdateCommentAsync(conn, key, commentId, body.Trim(), ct));
    }

    public async Task DeleteCommentAsync(Guid itemId, Guid userId, string commentId, CancellationToken ct = default)
    {
        var (conn, key) = await ResolveAsync(itemId, userId, ct);
        await _gateway.DeleteCommentAsync(conn, key, commentId, ct);
    }

    // ── Attachments ──
    public async Task<IReadOnlyList<JiraAttachmentDto>> GetAttachmentsAsync(Guid itemId, Guid userId, CancellationToken ct = default)
    {
        var (conn, key) = await ResolveAsync(itemId, userId, ct);
        var attachments = await _gateway.GetAttachmentsAsync(conn, key, ct);
        return attachments.Select(ToDto).ToList();
    }

    public async Task<(byte[] Data, string MimeType, string Filename)> DownloadAttachmentAsync(Guid itemId, Guid userId, string attachmentId, CancellationToken ct = default)
    {
        var (conn, key) = await ResolveAsync(itemId, userId, ct);
        // Tra filename/mime từ metadata để đặt tên khi tải + fallback content-type.
        var att = (await _gateway.GetAttachmentsAsync(conn, key, ct)).FirstOrDefault(a => a.Id == attachmentId)
            ?? throw new NotFoundException("Attachment", attachmentId);
        var content = await _gateway.DownloadAttachmentAsync(conn, attachmentId, att.Filename, att.MimeType ?? "application/octet-stream", ct);
        return (content.Data, content.MimeType, content.Filename);
    }

    public async Task<IReadOnlyList<JiraAttachmentDto>> UploadAttachmentAsync(Guid itemId, Guid userId, string filename, string mimeType, byte[] data, CancellationToken ct = default)
    {
        if (data.Length == 0) throw new BusinessRuleException("File rỗng.");
        var (conn, key) = await ResolveAsync(itemId, userId, ct);
        var created = await _gateway.UploadAttachmentAsync(conn, key, filename, mimeType, data, ct);
        return created.Select(ToDto).ToList();
    }

    public async Task DeleteAttachmentAsync(Guid itemId, Guid userId, string attachmentId, CancellationToken ct = default)
    {
        var (conn, _) = await ResolveAsync(itemId, userId, ct);
        await _gateway.DeleteAttachmentAsync(conn, attachmentId, ct);
    }
}
