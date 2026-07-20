using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

/// <summary>
/// Unit test cho các nhánh CHƯA cover của <see cref="SendEmailService"/>:
/// SendAsync (validate connection), GetSignatureAsync, GetThreadAsync, ReplyAsync, ForwardAsync,
/// và các nhánh lỗi của SendDraftAsync / DiscardDraftAsync.
/// Draft happy-path + attachment + gợi ý danh bạ đã có ở file test khác — không lặp lại ở đây.
/// </summary>
public class SendEmailServiceThreadTests
{
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IGmailGateway> _gmail = new();
    private readonly Mock<IGoogleContactRepository> _googleContacts = new();
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<IFolderRepository> _folders = new();
    private readonly GoogleContactMapper _mapper = new();
    private readonly Mock<ILogger<SendEmailService>> _logger = new();
    private readonly SendEmailService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _connId = Guid.NewGuid();

    public SendEmailServiceThreadTests()
    {
        _service = new SendEmailService(
            _connections.Object, _gmail.Object, _googleContacts.Object, _mapper,
            _items.Object, _folders.Object, _logger.Object);
    }

    // ───────────── Helper ─────────────

    /// <summary>Connection Gmail hợp lệ (Active, thuộc <paramref name="ownerId"/>).</summary>
    private Connection SetupConnection(
        Guid? ownerId = null,
        ServiceType serviceType = ServiceType.Gmail,
        ConnectionStatus status = ConnectionStatus.Active,
        string providerAccountId = "me@gmail.com")
    {
        var conn = new Connection
        {
            Id = _connId,
            UserId = ownerId ?? _userId,
            ProviderAccountId = providerAccountId,
            ServiceType = serviceType,
            Status = status
        };

        _connections.Setup(c => c.GetByIdTrackedAsync(_connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(conn);

        return conn;
    }

    /// <summary>Item email thuộc user, gắn với connection Gmail ở trên.</summary>
    private Item SetupItem(
        string metadataJson,
        Guid? ownerId = null,
        string? externalId = "msg-1",
        string title = "Hello",
        Guid? connectionId = null)
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            UserId = ownerId ?? _userId,
            ConnectionId = connectionId ?? _connId,
            Type = ItemType.Email,
            Title = title,
            Snippet = "snippet",
            ExternalId = externalId,
            ThreadId = "thread-1",
            MetadataJson = metadataJson,
            OccurredAt = DateTime.UtcNow
        };

        _items.Setup(i => i.GetByIdAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        return item;
    }

    private static GmailThreadMessage Msg(
        string messageId,
        string? from = "alice@x.com",
        IReadOnlyList<string>? to = null,
        IReadOnlyList<string>? cc = null,
        string? subject = "Hello",
        string? bodyHtml = "<p>body</p>",
        bool hasAttachment = false,
        IReadOnlyList<GmailAttachmentInfo>? attachments = null)
        => new(
            messageId,
            from,
            to ?? new List<string> { "me@gmail.com" },
            cc ?? new List<string>(),
            new List<string>(),
            subject,
            bodyHtml,
            "plain",
            DateTimeOffset.UtcNow,
            IsUnread: false,
            IsStarred: false,
            HasAttachment: hasAttachment,
            Labels: new List<string> { "INBOX" },
            Attachments: attachments ?? new List<GmailAttachmentInfo>());

    /// <summary>Setup mặc định cho SendInThreadAsync (reply/forward) — trả messageId cố định.</summary>
    private void SetupSendInThread(string messageId = "sent-1")
    {
        _gmail.Setup(g => g.SendInThreadAsync(
                It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<GmailAttachmentData>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(messageId);
    }

    // ────────────────────────── SendAsync ──────────────────────────

    [Fact]
    public async Task SendAsync_ValidRequest_CallsGatewayWithRecipientsAndReturnsMessageId()
    {
        var conn = SetupConnection();

        _gmail.Setup(g => g.SendMessageAsync(
                It.IsAny<Connection>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<GmailAttachmentData>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("gmail-msg-1");

        var request = new SendEmailRequest
        {
            ConnectionId = _connId,
            To = new List<string> { "bob@x.com" },
            Cc = new List<string> { "carol@x.com" },
            Bcc = new List<string> { "dave@x.com" },
            Subject = "Chào",
            BodyHtml = "<p>nội dung</p>"
        };

        var before = DateTime.UtcNow.AddSeconds(-1);
        var result = await _service.SendAsync(_userId, request);

        result.MessageId.Should().Be("gmail-msg-1");
        result.SentAt.Should().BeOnOrAfter(before);

        _gmail.Verify(g => g.SendMessageAsync(
            conn,
            It.Is<IReadOnlyList<string>>(t => t.SequenceEqual(new[] { "bob@x.com" })),
            It.Is<IReadOnlyList<string>>(c => c.SequenceEqual(new[] { "carol@x.com" })),
            It.Is<IReadOnlyList<string>>(b => b.SequenceEqual(new[] { "dave@x.com" })),
            "Chào", "<p>nội dung</p>", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_ConnectionNotFound_ThrowsNotFound()
    {
        _connections.Setup(c => c.GetByIdTrackedAsync(_connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        var request = new SendEmailRequest
        {
            ConnectionId = _connId,
            To = new List<string> { "bob@x.com" },
            Subject = "s",
            BodyHtml = "b"
        };

        var act = () => _service.SendAsync(_userId, request);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SendAsync_ConnectionOfAnotherUser_ThrowsNotFound()
    {
        // Connection tồn tại nhưng thuộc user khác → 404 (không lộ sự tồn tại).
        SetupConnection(ownerId: Guid.NewGuid());

        var request = new SendEmailRequest
        {
            ConnectionId = _connId,
            To = new List<string> { "bob@x.com" },
            Subject = "s",
            BodyHtml = "b"
        };

        var act = () => _service.SendAsync(_userId, request);

        await act.Should().ThrowAsync<NotFoundException>();
        _gmail.Verify(g => g.SendMessageAsync(
            It.IsAny<Connection>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<GmailAttachmentData>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_NonGmailConnection_ThrowsBusinessRule()
    {
        SetupConnection(serviceType: ServiceType.Drive);

        var request = new SendEmailRequest
        {
            ConnectionId = _connId,
            To = new List<string> { "bob@x.com" },
            Subject = "s",
            BodyHtml = "b"
        };

        var act = () => _service.SendAsync(_userId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SendAsync_InactiveConnection_ThrowsBusinessRule()
    {
        SetupConnection(status: ConnectionStatus.Disconnected);

        var request = new SendEmailRequest
        {
            ConnectionId = _connId,
            To = new List<string> { "bob@x.com" },
            Subject = "s",
            BodyHtml = "b"
        };

        var act = () => _service.SendAsync(_userId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SendAsync_GatewayThrowsProviderException_Propagates()
    {
        SetupConnection();

        _gmail.Setup(g => g.SendMessageAsync(
                It.IsAny<Connection>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<GmailAttachmentData>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderException("Gmail 500"));

        var request = new SendEmailRequest
        {
            ConnectionId = _connId,
            To = new List<string> { "bob@x.com" },
            Subject = "s",
            BodyHtml = "b"
        };

        var act = () => _service.SendAsync(_userId, request);

        await act.Should().ThrowAsync<ProviderException>();
    }

    // ───────────── GetSignatureAsync ─────────────

    [Fact]
    public async Task GetSignatureAsync_ValidConnection_ReturnsSignatureFromGateway()
    {
        var conn = SetupConnection();
        _gmail.Setup(g => g.GetSignatureAsync(conn, It.IsAny<CancellationToken>()))
            .ReturnsAsync("<p>Trân trọng</p>");

        var result = await _service.GetSignatureAsync(_userId, _connId);

        result.Should().Be("<p>Trân trọng</p>");
        _gmail.Verify(g => g.GetSignatureAsync(conn, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSignatureAsync_NoSignatureConfigured_ReturnsNull()
    {
        var conn = SetupConnection();
        _gmail.Setup(g => g.GetSignatureAsync(conn, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await _service.GetSignatureAsync(_userId, _connId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSignatureAsync_ConnectionOfAnotherUser_ThrowsNotFound()
    {
        SetupConnection(ownerId: Guid.NewGuid());

        var act = () => _service.GetSignatureAsync(_userId, _connId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetSignatureAsync_NonGmailConnection_ThrowsBusinessRule()
    {
        SetupConnection(serviceType: ServiceType.GCal);

        var act = () => _service.GetSignatureAsync(_userId, _connId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    // ───────────── GetThreadAsync ─────────────

    [Fact]
    public async Task GetThreadAsync_ValidItem_MapsMessagesAndLinksLocalItemIds()
    {
        SetupConnection();
        var item = SetupItem("{\"threadId\":\"thread-1\"}");

        _gmail.Setup(g => g.GetThreadAsync(It.IsAny<Connection>(), "thread-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailThread("thread-1", "Hello", new List<GmailThreadMessage>
            {
                Msg("msg-1"),
                Msg("msg-2", from: "bob@x.com")
            }));

        // Chỉ msg-1 có Item local → message thứ 2 phải trả ItemId = null.
        _items.Setup(i => i.GetTrackedByConnectionIdAsync(_connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item> { ["msg-1"] = item });

        var result = await _service.GetThreadAsync(_userId, item.Id);

        result.ThreadId.Should().Be("thread-1");
        result.Subject.Should().Be("Hello");
        result.OwnerEmail.Should().Be("me@gmail.com");
        result.Messages.Should().HaveCount(2);
        result.Messages[0].MessageId.Should().Be("msg-1");
        result.Messages[0].ItemId.Should().Be(item.Id);
        result.Messages[1].ItemId.Should().BeNull();
        _gmail.Verify(g => g.GetThreadAsync(It.IsAny<Connection>(), "thread-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetThreadAsync_MessageMissingFields_FallsBackToLocalMetadata()
    {
        SetupConnection();
        // Metadata local giữ bản nháp/bản đã gửi mà Gmail chưa trả body → service phải lấp chỗ trống.
        var item = SetupItem(
            "{\"threadId\":\"thread-1\",\"subject\":\"Local subject\",\"bodyHtml\":\"<p>local</p>\"," +
            "\"to\":[\"bob@x.com\"],\"cc\":[\"carol@x.com\"],\"bcc\":[\"dan@x.com\"]}");

        _gmail.Setup(g => g.GetThreadAsync(It.IsAny<Connection>(), "thread-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailThread("thread-1", null, new List<GmailThreadMessage>
            {
                Msg("msg-1", subject: null, bodyHtml: null, to: new List<string>(), cc: new List<string>())
            }));

        _items.Setup(i => i.GetTrackedByConnectionIdAsync(_connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item> { ["msg-1"] = item });

        var result = await _service.GetThreadAsync(_userId, item.Id);

        var msg = result.Messages[0];
        msg.BodyHtml.Should().Be("<p>local</p>");
        msg.Subject.Should().Be("Local subject");
        msg.To.Should().BeEquivalentTo(new[] { "bob@x.com" });
        msg.Cc.Should().BeEquivalentTo(new[] { "carol@x.com" });
        msg.Bcc.Should().BeEquivalentTo(new[] { "dan@x.com" });
    }

    [Fact]
    public async Task GetThreadAsync_ItemNotFound_ThrowsNotFound()
    {
        var itemId = Guid.NewGuid();
        _items.Setup(i => i.GetByIdAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);

        var act = () => _service.GetThreadAsync(_userId, itemId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetThreadAsync_ItemOfAnotherUserNotShared_ThrowsNotFound()
    {
        SetupConnection();
        var item = SetupItem("{\"threadId\":\"thread-1\"}", ownerId: Guid.NewGuid());

        _folders.Setup(f => f.IsItemSharedWithUserAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => _service.GetThreadAsync(_userId, item.Id);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetThreadAsync_ItemWithoutThreadIdInMetadata_ThrowsBusinessRule()
    {
        SetupConnection();
        var item = SetupItem("{}");

        var act = () => _service.GetThreadAsync(_userId, item.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    // ───────────── ReplyAsync ─────────────

    [Fact]
    public async Task ReplyAsync_SimpleReply_SendsToOriginalSenderWithRePrefix()
    {
        var conn = SetupConnection();
        var item = SetupItem(
            "{\"threadId\":\"thread-1\",\"from\":\"alice@x.com\",\"rfc822MessageId\":\"<abc@mail.gmail.com>\"}");
        SetupSendInThread();

        var request = new ReplyEmailRequest
        {
            ConnectionId = _connId,
            ItemId = item.Id,
            BodyHtml = "<p>ok</p>",
            ReplyAll = false
        };

        var result = await _service.ReplyAsync(_userId, request);

        result.MessageId.Should().Be("sent-1");
        result.ThreadId.Should().Be("thread-1");

        _gmail.Verify(g => g.SendInThreadAsync(
            conn, "thread-1", "<abc@mail.gmail.com>",
            It.Is<IReadOnlyList<string>>(t => t.SequenceEqual(new[] { "alice@x.com" })),
            It.Is<IReadOnlyList<string>>(c => c.Count == 0),
            It.Is<IReadOnlyList<string>>(b => b.Count == 0),
            "Re: Hello", "<p>ok</p>", null, It.IsAny<CancellationToken>()), Times.Once);
        // Reply thường KHÔNG cần gọi thêm Gmail để lấy Cc live.
        _gmail.Verify(g => g.GetMessageAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReplyAsync_ReplyAll_KeepsOriginalRecipientsButExcludesSelf()
    {
        SetupConnection();
        var item = SetupItem(
            "{\"threadId\":\"thread-1\",\"from\":\"alice@x.com\"," +
            "\"to\":[\"me@gmail.com\",\"bob@x.com\"],\"cc\":[\"carol@x.com\",\"me@gmail.com\"]}");
        SetupSendInThread();

        var request = new ReplyEmailRequest
        {
            ConnectionId = _connId,
            ItemId = item.Id,
            BodyHtml = "<p>ok</p>",
            ReplyAll = true
        };

        await _service.ReplyAsync(_userId, request);

        _gmail.Verify(g => g.SendInThreadAsync(
            It.IsAny<Connection>(), "thread-1", It.IsAny<string?>(),
            It.Is<IReadOnlyList<string>>(t => t.SequenceEqual(new[] { "alice@x.com", "bob@x.com" })),
            It.Is<IReadOnlyList<string>>(c => c.SequenceEqual(new[] { "carol@x.com" })),
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<GmailAttachmentData>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_ReplyAllWithoutCcInMetadata_FetchesLiveMessageForCc()
    {
        SetupConnection();
        // Email cũ sync trước khi lưu Cc → metadata không có key "cc" ⇒ phải hỏi Gmail bản live.
        var item = SetupItem("{\"threadId\":\"thread-1\",\"from\":\"alice@x.com\",\"to\":[\"bob@x.com\"]}");
        SetupSendInThread();

        _gmail.Setup(g => g.GetMessageAsync(It.IsAny<Connection>(), "msg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailMessage(
                "msg-1", "thread-1", "Hello", "alice@x.com",
                new List<string> { "me@gmail.com", "bob@x.com" },
                new List<string> { "dave@x.com" },
                new List<string>(),
                "snippet", new List<string> { "INBOX" }, false, DateTimeOffset.UtcNow));

        var request = new ReplyEmailRequest
        {
            ConnectionId = _connId,
            ItemId = item.Id,
            BodyHtml = "<p>ok</p>",
            ReplyAll = true
        };

        await _service.ReplyAsync(_userId, request);

        _gmail.Verify(g => g.GetMessageAsync(It.IsAny<Connection>(), "msg-1", It.IsAny<CancellationToken>()), Times.Once);
        _gmail.Verify(g => g.SendInThreadAsync(
            It.IsAny<Connection>(), "thread-1", It.IsAny<string?>(),
            It.Is<IReadOnlyList<string>>(t => t.SequenceEqual(new[] { "alice@x.com", "bob@x.com" })),
            It.Is<IReadOnlyList<string>>(c => c.SequenceEqual(new[] { "dave@x.com" })),
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<GmailAttachmentData>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_SubjectAlreadyPrefixed_DoesNotDuplicateRe()
    {
        SetupConnection();
        var item = SetupItem("{\"threadId\":\"thread-1\",\"from\":\"alice@x.com\"}", title: "Re: Hello");
        SetupSendInThread();

        var request = new ReplyEmailRequest
        {
            ConnectionId = _connId,
            ItemId = item.Id,
            BodyHtml = "<p>ok</p>"
        };

        await _service.ReplyAsync(_userId, request);

        _gmail.Verify(g => g.SendInThreadAsync(
            It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
            "Re: Hello", It.IsAny<string>(),
            It.IsAny<IReadOnlyList<GmailAttachmentData>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_ItemWithoutThreadIdInMetadata_ThrowsBusinessRule()
    {
        SetupConnection();
        var item = SetupItem("{\"from\":\"alice@x.com\"}");

        var request = new ReplyEmailRequest
        {
            ConnectionId = _connId,
            ItemId = item.Id,
            BodyHtml = "<p>ok</p>"
        };

        var act = () => _service.ReplyAsync(_userId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task ReplyAsync_ConnectionIdMismatch_ThrowsBusinessRule()
    {
        SetupConnection();
        var item = SetupItem("{\"threadId\":\"thread-1\",\"from\":\"alice@x.com\"}");

        var request = new ReplyEmailRequest
        {
            ConnectionId = Guid.NewGuid(), // khác connection của item
            ItemId = item.Id,
            BodyHtml = "<p>ok</p>"
        };

        var act = () => _service.ReplyAsync(_userId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task ReplyAsync_SharedViewer_ThrowsForbidden()
    {
        SetupConnection();
        var item = SetupItem("{\"threadId\":\"thread-1\",\"from\":\"alice@x.com\"}", ownerId: Guid.NewGuid());

        // Được XEM item trong folder chia sẻ nhưng không phải Editor → 403 chứ không phải 404.
        _folders.Setup(f => f.IsItemSharedWithUserAsEditorAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _folders.Setup(f => f.IsItemSharedWithUserAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new ReplyEmailRequest
        {
            ConnectionId = _connId,
            ItemId = item.Id,
            BodyHtml = "<p>ok</p>"
        };

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _service.ReplyAsync(_userId, request));

        ex.Code.Should().Be(ErrorCodes.SharedViewerReadOnly);
    }

    // ───────────── ForwardAsync ─────────────

    [Fact]
    public async Task ForwardAsync_ValidItem_PrefixesFwdAndAppendsOriginalBody()
    {
        var conn = SetupConnection();
        var item = SetupItem("{\"threadId\":\"thread-1\"}");
        SetupSendInThread("fwd-1");

        _gmail.Setup(g => g.GetMessageAsync(It.IsAny<Connection>(), "msg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailMessage(
                "msg-1", "thread-1", "Hello", "alice@x.com",
                new List<string> { "me@gmail.com" }, new List<string>(), new List<string>(),
                "snippet", new List<string> { "INBOX" }, false, DateTimeOffset.UtcNow,
                BodyHtml: "<p>nội dung gốc</p>"));

        var request = new ForwardEmailRequest
        {
            ConnectionId = _connId,
            ItemId = item.Id,
            To = new List<string> { "dave@x.com" },
            BodyHtml = "<p>FYI</p>",
            IncludeAttachments = false
        };

        var result = await _service.ForwardAsync(_userId, request);

        result.MessageId.Should().Be("fwd-1");
        result.ThreadId.Should().Be("thread-1");

        _gmail.Verify(g => g.SendInThreadAsync(
            conn, "thread-1", null,
            It.Is<IReadOnlyList<string>>(t => t.SequenceEqual(new[] { "dave@x.com" })),
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
            "Fwd: Hello",
            It.Is<string>(b => b.Contains("<p>FYI</p>")
                               && b.Contains("Forwarded message")
                               && b.Contains("<p>nội dung gốc</p>")
                               && b.Contains("alice@x.com")),
            null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ForwardAsync_IncludeAttachments_DownloadsOriginalAttachmentsAndAttaches()
    {
        SetupConnection();
        var item = SetupItem("{\"threadId\":\"thread-1\"}");
        SetupSendInThread("fwd-2");

        _gmail.Setup(g => g.GetMessageAsync(It.IsAny<Connection>(), "msg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailMessage(
                "msg-1", "thread-1", "Hello", "alice@x.com",
                new List<string> { "me@gmail.com" }, new List<string>(), new List<string>(),
                "snippet", new List<string> { "INBOX" }, HasAttachment: true, DateTimeOffset.UtcNow,
                BodyHtml: "<p>nội dung gốc</p>"));

        _gmail.Setup(g => g.GetThreadAsync(It.IsAny<Connection>(), "thread-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailThread("thread-1", "Hello", new List<GmailThreadMessage>
            {
                Msg("msg-1", hasAttachment: true, attachments: new List<GmailAttachmentInfo>
                {
                    new("att-1", "bao-cao.pdf", "application/pdf", 11)
                })
            }));

        _gmail.Setup(g => g.GetAttachmentAsync(
                It.IsAny<Connection>(), "msg-1", "att-1", "bao-cao.pdf", "application/pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailAttachmentData(Encoding.UTF8.GetBytes("hello world"), "bao-cao.pdf", "application/pdf", 11));

        var request = new ForwardEmailRequest
        {
            ConnectionId = _connId,
            ItemId = item.Id,
            To = new List<string> { "dave@x.com" },
            BodyHtml = "<p>FYI</p>",
            IncludeAttachments = true
        };

        await _service.ForwardAsync(_userId, request);

        _gmail.Verify(g => g.GetAttachmentAsync(
            It.IsAny<Connection>(), "msg-1", "att-1", "bao-cao.pdf", "application/pdf", It.IsAny<CancellationToken>()), Times.Once);
        _gmail.Verify(g => g.SendInThreadAsync(
            It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<string>(), It.IsAny<string>(),
            It.Is<IReadOnlyList<GmailAttachmentData>?>(a => a != null && a.Count == 1 && a[0].Filename == "bao-cao.pdf"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ForwardAsync_SubjectAlreadyPrefixed_DoesNotDuplicateFwd()
    {
        SetupConnection();
        var item = SetupItem("{\"threadId\":\"thread-1\"}", title: "Fwd: Hello");
        SetupSendInThread();

        _gmail.Setup(g => g.GetMessageAsync(It.IsAny<Connection>(), "msg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailMessage(
                "msg-1", "thread-1", "Fwd: Hello", "alice@x.com",
                new List<string>(), new List<string>(), new List<string>(),
                "snippet", new List<string>(), false, DateTimeOffset.UtcNow, BodyHtml: "<p>gốc</p>"));

        var request = new ForwardEmailRequest
        {
            ConnectionId = _connId,
            ItemId = item.Id,
            To = new List<string> { "dave@x.com" },
            BodyHtml = "<p>FYI</p>",
            IncludeAttachments = false
        };

        await _service.ForwardAsync(_userId, request);

        _gmail.Verify(g => g.SendInThreadAsync(
            It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
            "Fwd: Hello", It.IsAny<string>(),
            It.IsAny<IReadOnlyList<GmailAttachmentData>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ForwardAsync_ItemWithoutThreadIdInMetadata_ThrowsBusinessRule()
    {
        SetupConnection();
        var item = SetupItem("{}");

        var request = new ForwardEmailRequest
        {
            ConnectionId = _connId,
            ItemId = item.Id,
            To = new List<string> { "dave@x.com" },
            BodyHtml = "<p>FYI</p>"
        };

        var act = () => _service.ForwardAsync(_userId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
        _gmail.Verify(g => g.GetMessageAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForwardAsync_ItemWithoutExternalId_ThrowsBusinessRule()
    {
        SetupConnection();
        var item = SetupItem("{\"threadId\":\"thread-1\"}", externalId: null);

        var request = new ForwardEmailRequest
        {
            ConnectionId = _connId,
            ItemId = item.Id,
            To = new List<string> { "dave@x.com" },
            BodyHtml = "<p>FYI</p>"
        };

        var act = () => _service.ForwardAsync(_userId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task ForwardAsync_SharedViewer_ThrowsForbidden()
    {
        SetupConnection();
        var item = SetupItem("{\"threadId\":\"thread-1\"}", ownerId: Guid.NewGuid());

        _folders.Setup(f => f.IsItemSharedWithUserAsEditorAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _folders.Setup(f => f.IsItemSharedWithUserAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new ForwardEmailRequest
        {
            ConnectionId = _connId,
            ItemId = item.Id,
            To = new List<string> { "dave@x.com" },
            BodyHtml = "<p>FYI</p>"
        };

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _service.ForwardAsync(_userId, request));

        ex.Code.Should().Be(ErrorCodes.SharedViewerReadOnly);
    }

    // ───────────── SendDraftAsync (nhánh lỗi + resolve draftId) ─────────────

    [Fact]
    public async Task SendDraftAsync_ItemNotFound_ThrowsNotFound()
    {
        var itemId = Guid.NewGuid();
        _items.Setup(i => i.GetByIdAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);

        var act = () => _service.SendDraftAsync(_userId, itemId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SendDraftAsync_DraftOfAnotherUserNotShared_ThrowsNotFound()
    {
        SetupConnection();
        var item = SetupItem("{\"draftId\":\"draft-1\"}", ownerId: Guid.NewGuid());
        item.ThreadId = null; // không có thread gốc để suy quyền

        _folders.Setup(f => f.IsItemSharedWithUserAsEditorAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => _service.SendDraftAsync(_userId, item.Id);

        await act.Should().ThrowAsync<NotFoundException>();
        _gmail.Verify(g => g.SendDraftAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendDraftAsync_ItemWithoutConnection_ThrowsBusinessRule()
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            ConnectionId = null,
            Type = ItemType.Email,
            Title = "draft",
            Snippet = "",
            MetadataJson = "{\"draftId\":\"draft-1\"}"
        };
        _items.Setup(i => i.GetByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);

        var act = () => _service.SendDraftAsync(_userId, item.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SendDraftAsync_NonGmailConnection_ThrowsBusinessRule()
    {
        SetupConnection(serviceType: ServiceType.Drive);
        var item = SetupItem("{\"draftId\":\"draft-1\"}");

        var act = () => _service.SendDraftAsync(_userId, item.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SendDraftAsync_MetadataWithoutDraftId_ResolvesDraftIdFromGateway()
    {
        SetupConnection();
        var item = SetupItem("{\"labels\":[\"DRAFT\"]}");

        _gmail.Setup(g => g.GetDraftIdByMessageIdAsync(It.IsAny<Connection>(), "msg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("draft-resolved");
        _gmail.Setup(g => g.SendDraftAsync(It.IsAny<Connection>(), "draft-resolved", It.IsAny<CancellationToken>()))
            .ReturnsAsync("msg-sent");

        var result = await _service.SendDraftAsync(_userId, item.Id);

        result.MessageId.Should().Be("msg-sent");
        item.ExternalId.Should().Be("msg-sent");
        _gmail.Verify(g => g.SendDraftAsync(It.IsAny<Connection>(), "draft-resolved", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendDraftAsync_DraftCannotBeResolved_ThrowsBusinessRule()
    {
        SetupConnection();
        var item = SetupItem("{\"labels\":[\"DRAFT\"]}");

        // Nháp đã bị xoá/gửi trên Gmail → không resolve được draftId.
        _gmail.Setup(g => g.GetDraftIdByMessageIdAsync(It.IsAny<Connection>(), "msg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var act = () => _service.SendDraftAsync(_userId, item.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    // ───────────── DiscardDraftAsync (nhánh lỗi) ─────────────

    [Fact]
    public async Task DiscardDraftAsync_ItemNotFound_ThrowsNotFound()
    {
        var itemId = Guid.NewGuid();
        _items.Setup(i => i.GetByIdAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);

        var act = () => _service.DiscardDraftAsync(_userId, itemId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task DiscardDraftAsync_DraftOfAnotherUserNotShared_ThrowsNotFound()
    {
        SetupConnection();
        var item = SetupItem("{\"draftId\":\"draft-1\"}", ownerId: Guid.NewGuid());
        item.ThreadId = null;

        _folders.Setup(f => f.IsItemSharedWithUserAsEditorAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => _service.DiscardDraftAsync(_userId, item.Id);

        await act.Should().ThrowAsync<NotFoundException>();
        _gmail.Verify(g => g.DeleteDraftAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DiscardDraftAsync_ItemWithoutConnection_ThrowsBusinessRule()
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            ConnectionId = null,
            Type = ItemType.Email,
            Title = "draft",
            Snippet = "",
            MetadataJson = "{\"draftId\":\"draft-1\"}"
        };
        _items.Setup(i => i.GetByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);

        var act = () => _service.DiscardDraftAsync(_userId, item.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task DiscardDraftAsync_DraftWithoutExternalId_SkipsGatewayButRemovesLocalItem()
    {
        SetupConnection();
        var item = SetupItem("{\"draftId\":\"draft-1\"}", externalId: null);

        await _service.DiscardDraftAsync(_userId, item.Id);

        _gmail.Verify(g => g.DeleteDraftAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _items.Verify(i => i.Remove(item), Times.Once);
        _items.Verify(i => i.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
