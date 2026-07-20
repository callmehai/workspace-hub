using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

/// <summary>
/// Unit test cho <see cref="JiraTicketService"/> — comment + attachment của ticket Jira.
/// Mọi method đều đi qua ResolveAsync (ownership / shared-Editor / Type=Ticket / connection Jira Active),
/// nên nhánh lỗi chung được test kỹ ở block GetCommentsAsync và spot-check lại ở các method còn lại.
/// </summary>
public class JiraTicketServiceTests
{
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IJiraGateway> _gateway = new();
    private readonly Mock<IFolderRepository> _folders = new();
    private readonly JiraTicketService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _itemId = Guid.NewGuid();
    private readonly Guid _connId = Guid.NewGuid();

    public JiraTicketServiceTests()
    {
        _service = new JiraTicketService(_items.Object, _connections.Object, _gateway.Object, _folders.Object);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private Connection Conn(ServiceType type = ServiceType.Jira, ConnectionStatus status = ConnectionStatus.Active) =>
        new()
        {
            Id = _connId,
            UserId = _userId,
            ServiceType = type,
            ProviderAccountId = "cloud-1",
            Status = status
        };

    private Item Ticket(ItemType type = ItemType.Ticket, string? externalId = "SCRUM-1", Guid? connId = null) =>
        new()
        {
            Id = _itemId,
            UserId = _userId,
            Type = type,
            Title = "Fix the bug",
            ExternalId = externalId,
            ConnectionId = connId ?? _connId
        };

    /// <summary>Dựng đường đi "sạch": item thuộc user + connection Jira Active.</summary>
    private Connection SetupResolvable(Item? item = null, Connection? conn = null)
    {
        var theItem = item ?? Ticket();
        var theConn = conn ?? Conn();
        _items.Setup(m => m.GetByIdAndUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(theItem);
        _connections.Setup(m => m.GetByIdAsync(theItem.ConnectionId ?? _connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(theConn);
        return theConn;
    }

    /// <summary>Item không tồn tại với user này (dùng cho nhánh 404 / shared).</summary>
    private void SetupItemMissing() =>
        _items.Setup(m => m.GetByIdAndUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);

    private static JiraComment Comment(string id = "c1", string body = "Hello") =>
        new(id, body, "Alice", "acc-1",
            new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 2, 10, 0, 0, TimeSpan.Zero));

    private static JiraAttachment Attachment(string id = "a1", string filename = "spec.pdf",
        string? mime = "application/pdf", string? contentUrl = null) =>
        new(id, filename, mime, 1024, "Alice",
            new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero), contentUrl);

    // ── GetCommentsAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetComments_HappyPath_MapsGatewayCommentsToDto()
    {
        var conn = SetupResolvable();
        _gateway.Setup(m => m.GetCommentsAsync(conn, "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JiraComment> { Comment("c1", "Đã fix"), Comment("c2", "Ok") });

        var result = await _service.GetCommentsAsync(_itemId, _userId);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be("c1");
        result[0].Body.Should().Be("Đã fix");       // JiraComment.BodyText → JiraCommentDto.Body
        result[0].AuthorName.Should().Be("Alice");
        result[0].AuthorAccountId.Should().Be("acc-1");
        result[1].Id.Should().Be("c2");
    }

    [Fact]
    public async Task GetComments_ItemNotFoundAndNotShared_ThrowsNotFound()
    {
        SetupItemMissing();
        _folders.Setup(m => m.IsItemSharedWithUserAsEditorAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _folders.Setup(m => m.IsItemSharedWithUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => _service.GetCommentsAsync(_itemId, _userId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetComments_SharedViewer_ThrowsForbiddenWithReadOnlyCode()
    {
        SetupItemMissing();
        _folders.Setup(m => m.IsItemSharedWithUserAsEditorAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _folders.Setup(m => m.IsItemSharedWithUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var act = () => _service.GetCommentsAsync(_itemId, _userId);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be(ErrorCodes.SharedViewerReadOnly);
    }

    [Fact]
    public async Task GetComments_SharedEditor_ResolvesItemViaGetByIdAsync()
    {
        // Item không thuộc user nhưng user là Editor của folder được share → vẫn đọc được.
        SetupItemMissing();
        _folders.Setup(m => m.IsItemSharedWithUserAsEditorAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _items.Setup(m => m.GetByIdAsync(_itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ticket());
        var conn = Conn();
        _connections.Setup(m => m.GetByIdAsync(_connId, It.IsAny<CancellationToken>())).ReturnsAsync(conn);
        _gateway.Setup(m => m.GetCommentsAsync(conn, "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JiraComment> { Comment() });

        var result = await _service.GetCommentsAsync(_itemId, _userId);

        result.Should().ContainSingle();
        _items.Verify(m => m.GetByIdAsync(_itemId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetComments_ItemNotTicket_ThrowsBusinessRule()
    {
        SetupResolvable(Ticket(type: ItemType.Email));

        var act = () => _service.GetCommentsAsync(_itemId, _userId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task GetComments_TicketWithoutExternalId_ThrowsBusinessRule()
    {
        SetupResolvable(Ticket(externalId: null));

        var act = () => _service.GetCommentsAsync(_itemId, _userId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task GetComments_TicketWithoutConnection_ThrowsBusinessRule()
    {
        _items.Setup(m => m.GetByIdAndUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Item
            {
                Id = _itemId,
                UserId = _userId,
                Type = ItemType.Ticket,
                Title = "Fix the bug",
                ExternalId = "SCRUM-1",
                ConnectionId = null
            });

        var act = () => _service.GetCommentsAsync(_itemId, _userId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task GetComments_ConnectionNotFound_ThrowsNotFound()
    {
        _items.Setup(m => m.GetByIdAndUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ticket());
        _connections.Setup(m => m.GetByIdAsync(_connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        var act = () => _service.GetCommentsAsync(_itemId, _userId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetComments_ConnectionNotJira_ThrowsBusinessRule()
    {
        SetupResolvable(conn: Conn(type: ServiceType.Gmail));

        var act = () => _service.GetCommentsAsync(_itemId, _userId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task GetComments_InactiveConnection_ThrowsBusinessRule()
    {
        SetupResolvable(conn: Conn(status: ConnectionStatus.Error));

        var act = () => _service.GetCommentsAsync(_itemId, _userId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    // ── AddCommentAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task AddComment_HappyPath_TrimsBodyAndReturnsDto()
    {
        var conn = SetupResolvable();
        string? sentBody = null;
        _gateway.Setup(m => m.AddCommentAsync(conn, "SCRUM-1", It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback((Connection _, string _, string b, IEnumerable<string>? _, CancellationToken _) => sentBody = b)
            .ReturnsAsync(Comment("c9", "Đã xong"));

        var result = await _service.AddCommentAsync(_itemId, _userId, "  Đã xong  ");

        sentBody.Should().Be("Đã xong");            // body được Trim trước khi gửi
        result.Id.Should().Be("c9");
        result.Body.Should().Be("Đã xong");
    }

    [Fact]
    public async Task AddComment_EmptyBodyWithoutMedia_ThrowsBusinessRuleBeforeResolving()
    {
        var act = () => _service.AddCommentAsync(_itemId, _userId, "   ");

        await act.Should().ThrowAsync<BusinessRuleException>();
        // Validate xảy ra TRƯỚC ResolveAsync → không chạm repository.
        _items.Verify(m => m.GetByIdAndUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AddComment_WithMediaIds_AppendsSiteUrlAttachmentLinks()
    {
        var conn = SetupResolvable();
        _gateway.Setup(m => m.GetAttachmentsAsync(conn, "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JiraAttachment> { Attachment("a1", "spec.pdf") });
        _gateway.Setup(m => m.GetSiteUrlAsync(conn, It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://demo.atlassian.net/");   // dấu / cuối phải bị trim
        string? sentBody = null;
        _gateway.Setup(m => m.AddCommentAsync(conn, "SCRUM-1", It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback((Connection _, string _, string b, IEnumerable<string>? _, CancellationToken _) => sentBody = b)
            .ReturnsAsync(Comment());

        await _service.AddCommentAsync(_itemId, _userId, "Xem file", new List<string> { "a1" });

        sentBody.Should().Be("Xem file\n[📎 spec.pdf](https://demo.atlassian.net/rest/api/3/attachment/content/a1)");
    }

    [Fact]
    public async Task AddComment_EmptyBodyWithMedia_SendsOnlyLinks()
    {
        var conn = SetupResolvable();
        _gateway.Setup(m => m.GetAttachmentsAsync(conn, "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JiraAttachment> { Attachment("a1", "anh.png", "image/png") });
        _gateway.Setup(m => m.GetSiteUrlAsync(conn, It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://demo.atlassian.net");
        string? sentBody = null;
        _gateway.Setup(m => m.AddCommentAsync(conn, "SCRUM-1", It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback((Connection _, string _, string b, IEnumerable<string>? _, CancellationToken _) => sentBody = b)
            .ReturnsAsync(Comment());

        // Body rỗng vẫn hợp lệ khi có media → comment chỉ chứa link file.
        await _service.AddCommentAsync(_itemId, _userId, "", new List<string> { "a1" });

        sentBody.Should().Be("[📎 anh.png](https://demo.atlassian.net/rest/api/3/attachment/content/a1)");
    }

    [Fact]
    public async Task AddComment_SiteUrlNull_FallsBackToContentUrl()
    {
        var conn = SetupResolvable();
        _gateway.Setup(m => m.GetAttachmentsAsync(conn, "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JiraAttachment>
                { Attachment("a1", "spec.pdf", contentUrl: "https://api.atlassian.com/content/a1") });
        _gateway.Setup(m => m.GetSiteUrlAsync(conn, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        string? sentBody = null;
        _gateway.Setup(m => m.AddCommentAsync(conn, "SCRUM-1", It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback((Connection _, string _, string b, IEnumerable<string>? _, CancellationToken _) => sentBody = b)
            .ReturnsAsync(Comment());

        await _service.AddCommentAsync(_itemId, _userId, "Ghi chú", new List<string> { "a1" });

        sentBody.Should().Be("Ghi chú\n[📎 spec.pdf](https://api.atlassian.com/content/a1)");
    }

    [Fact]
    public async Task AddComment_UnknownMediaId_IgnoresLinkAndKeepsBody()
    {
        var conn = SetupResolvable();
        _gateway.Setup(m => m.GetAttachmentsAsync(conn, "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JiraAttachment> { Attachment("a1") });
        _gateway.Setup(m => m.GetSiteUrlAsync(conn, It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://demo.atlassian.net");
        string? sentBody = null;
        _gateway.Setup(m => m.AddCommentAsync(conn, "SCRUM-1", It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback((Connection _, string _, string b, IEnumerable<string>? _, CancellationToken _) => sentBody = b)
            .ReturnsAsync(Comment());

        // mediaId "khong-ton-tai" không match attachment nào → bỏ qua, body giữ nguyên.
        await _service.AddCommentAsync(_itemId, _userId, "Chỉ text", new List<string> { "khong-ton-tai" });

        sentBody.Should().Be("Chỉ text");
    }

    [Fact]
    public async Task AddComment_ItemNotFound_ThrowsNotFound()
    {
        SetupItemMissing();

        var act = () => _service.AddCommentAsync(_itemId, _userId, "Nội dung");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── UpdateCommentAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateComment_HappyPath_TrimsBodyAndReturnsDto()
    {
        var conn = SetupResolvable();
        _gateway.Setup(m => m.UpdateCommentAsync(conn, "SCRUM-1", "c1", "Đã sửa", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Comment("c1", "Đã sửa"));

        var result = await _service.UpdateCommentAsync(_itemId, _userId, "c1", "  Đã sửa  ");

        result.Id.Should().Be("c1");
        result.Body.Should().Be("Đã sửa");
        _gateway.Verify(m => m.UpdateCommentAsync(conn, "SCRUM-1", "c1", "Đã sửa", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateComment_EmptyBody_ThrowsBusinessRuleBeforeResolving()
    {
        var act = () => _service.UpdateCommentAsync(_itemId, _userId, "c1", "  ");

        await act.Should().ThrowAsync<BusinessRuleException>();
        _items.Verify(m => m.GetByIdAndUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateComment_ItemNotFound_ThrowsNotFound()
    {
        SetupItemMissing();

        var act = () => _service.UpdateCommentAsync(_itemId, _userId, "c1", "Nội dung");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── DeleteCommentAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task DeleteComment_HappyPath_CallsGatewayWithIssueKey()
    {
        var conn = SetupResolvable();

        await _service.DeleteCommentAsync(_itemId, _userId, "c1");

        _gateway.Verify(m => m.DeleteCommentAsync(conn, "SCRUM-1", "c1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteComment_ItemNotTicket_ThrowsBusinessRule()
    {
        SetupResolvable(Ticket(type: ItemType.File));

        var act = () => _service.DeleteCommentAsync(_itemId, _userId, "c1");

        await act.Should().ThrowAsync<BusinessRuleException>();
        _gateway.Verify(m => m.DeleteCommentAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteComment_SharedViewer_ThrowsForbidden()
    {
        SetupItemMissing();
        _folders.Setup(m => m.IsItemSharedWithUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var act = () => _service.DeleteCommentAsync(_itemId, _userId, "c1");

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    // ── GetAttachmentsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetAttachments_HappyPath_MapsGatewayAttachmentsToDto()
    {
        var conn = SetupResolvable();
        _gateway.Setup(m => m.GetAttachmentsAsync(conn, "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JiraAttachment>
            {
                Attachment("a1", "spec.pdf", "application/pdf"),
                Attachment("a2", "anh.png", "image/png")
            });

        var result = await _service.GetAttachmentsAsync(_itemId, _userId);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be("a1");
        result[0].Filename.Should().Be("spec.pdf");
        result[0].MimeType.Should().Be("application/pdf");
        result[0].Size.Should().Be(1024);
        result[1].Filename.Should().Be("anh.png");
    }

    [Fact]
    public async Task GetAttachments_ItemNotFound_ThrowsNotFound()
    {
        SetupItemMissing();

        var act = () => _service.GetAttachmentsAsync(_itemId, _userId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetAttachments_InactiveConnection_ThrowsBusinessRule()
    {
        SetupResolvable(conn: Conn(status: ConnectionStatus.Disconnected));

        var act = () => _service.GetAttachmentsAsync(_itemId, _userId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    // ── DownloadAttachmentAsync ─────────────────────────────────────────────

    [Fact]
    public async Task DownloadAttachment_HappyPath_ReturnsContentFromGateway()
    {
        var conn = SetupResolvable();
        _gateway.Setup(m => m.GetAttachmentsAsync(conn, "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JiraAttachment> { Attachment("a1", "spec.pdf", "application/pdf") });
        _gateway.Setup(m => m.DownloadAttachmentAsync(conn, "a1", "spec.pdf", "application/pdf",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JiraAttachmentContent(new byte[] { 1, 2, 3 }, "application/pdf", "spec.pdf"));

        var (data, mime, filename) = await _service.DownloadAttachmentAsync(_itemId, _userId, "a1");

        data.Should().Equal(new byte[] { 1, 2, 3 });
        mime.Should().Be("application/pdf");
        filename.Should().Be("spec.pdf");
    }

    [Fact]
    public async Task DownloadAttachment_NullMimeType_FallsBackToOctetStream()
    {
        var conn = SetupResolvable();
        _gateway.Setup(m => m.GetAttachmentsAsync(conn, "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JiraAttachment> { Attachment("a1", "khong-ro.bin", mime: null) });
        _gateway.Setup(m => m.DownloadAttachmentAsync(conn, "a1", "khong-ro.bin", "application/octet-stream",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JiraAttachmentContent(new byte[] { 9 }, "application/octet-stream", "khong-ro.bin"));

        var (_, mime, _) = await _service.DownloadAttachmentAsync(_itemId, _userId, "a1");

        mime.Should().Be("application/octet-stream");
        _gateway.Verify(m => m.DownloadAttachmentAsync(conn, "a1", "khong-ro.bin", "application/octet-stream",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DownloadAttachment_AttachmentIdNotOnIssue_ThrowsNotFound()
    {
        var conn = SetupResolvable();
        _gateway.Setup(m => m.GetAttachmentsAsync(conn, "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JiraAttachment> { Attachment("a1") });

        var act = () => _service.DownloadAttachmentAsync(_itemId, _userId, "khong-ton-tai");

        await act.Should().ThrowAsync<NotFoundException>();
        _gateway.Verify(m => m.DownloadAttachmentAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadAttachment_ItemNotFound_ThrowsNotFound()
    {
        SetupItemMissing();

        var act = () => _service.DownloadAttachmentAsync(_itemId, _userId, "a1");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── UploadAttachmentAsync ───────────────────────────────────────────────

    [Fact]
    public async Task UploadAttachment_HappyPath_ReturnsCreatedAttachmentDtos()
    {
        var conn = SetupResolvable();
        var data = new byte[] { 1, 2, 3, 4 };
        _gateway.Setup(m => m.UploadAttachmentAsync(conn, "SCRUM-1", "spec.pdf", "application/pdf", data,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JiraAttachment> { Attachment("a9", "spec.pdf", "application/pdf") });

        var result = await _service.UploadAttachmentAsync(_itemId, _userId, "spec.pdf", "application/pdf", data);

        result.Should().ContainSingle();
        result[0].Id.Should().Be("a9");
        result[0].Filename.Should().Be("spec.pdf");
        _gateway.Verify(m => m.UploadAttachmentAsync(conn, "SCRUM-1", "spec.pdf", "application/pdf", data,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadAttachment_EmptyData_ThrowsBusinessRuleBeforeResolving()
    {
        var act = () => _service.UploadAttachmentAsync(_itemId, _userId, "rong.txt", "text/plain", Array.Empty<byte>());

        await act.Should().ThrowAsync<BusinessRuleException>();
        _items.Verify(m => m.GetByIdAndUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadAttachment_SharedViewer_ThrowsForbidden()
    {
        SetupItemMissing();
        _folders.Setup(m => m.IsItemSharedWithUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var act = () => _service.UploadAttachmentAsync(_itemId, _userId, "a.txt", "text/plain", new byte[] { 1 });

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task UploadAttachment_ProviderRejects_PropagatesException()
    {
        var conn = SetupResolvable();
        _gateway.Setup(m => m.UploadAttachmentAsync(conn, "SCRUM-1", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessRuleException("Jira từ chối upload"));

        var act = () => _service.UploadAttachmentAsync(_itemId, _userId, "a.txt", "text/plain", new byte[] { 1 });

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    // ── DeleteAttachmentAsync ───────────────────────────────────────────────

    [Fact]
    public async Task DeleteAttachment_HappyPath_CallsGatewayWithAttachmentId()
    {
        var conn = SetupResolvable();

        await _service.DeleteAttachmentAsync(_itemId, _userId, "a1");

        // Xoá attachment không cần issue key — gateway chỉ nhận connection + attachmentId.
        _gateway.Verify(m => m.DeleteAttachmentAsync(conn, "a1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAttachment_ItemNotFound_ThrowsNotFound()
    {
        SetupItemMissing();

        var act = () => _service.DeleteAttachmentAsync(_itemId, _userId, "a1");

        await act.Should().ThrowAsync<NotFoundException>();
        _gateway.Verify(m => m.DeleteAttachmentAsync(It.IsAny<Connection>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAttachment_ConnectionNotJira_ThrowsBusinessRule()
    {
        SetupResolvable(conn: Conn(type: ServiceType.Drive));

        var act = () => _service.DeleteAttachmentAsync(_itemId, _userId, "a1");

        await act.Should().ThrowAsync<BusinessRuleException>();
    }
}
