using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

/// <summary>
/// Unit test cho các method CHƯA cover của <see cref="ItemService"/>:
/// GetTicketAssigneesAsync, CreateNoteAsync, GetCalendarEventDetailAsync, RsvpEventAsync, SendEmailToGuestsAsync.
/// (GetItemsAsync / GetItemByIdAsync / UpdateStatusAsync / ToggleImportantAsync đã có ở ItemServiceTests.)
/// </summary>
public class ItemServiceDetailTests
{
    private readonly Mock<IItemRepository> _itemRepo = new();
    private readonly Mock<IFolderRepository> _folderRepo = new();
    private readonly Mock<IConnectionHealthChecker> _healthChecker = new();
    private readonly Mock<IConnectionRepository> _connectionRepo = new();
    private readonly Mock<ICalendarGateway> _calendarGateway = new();
    private readonly Mock<ISendEmailService> _sendEmailService = new();
    private readonly Mock<ILogger<ItemService>> _logger = new();
    private readonly ItemService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _connId = Guid.NewGuid();

    public ItemServiceDetailTests()
    {
        _sut = new ItemService(
            _itemRepo.Object,
            _folderRepo.Object,
            _healthChecker.Object,
            _connectionRepo.Object,
            _calendarGateway.Object,
            _sendEmailService.Object,
            _logger.Object);
    }

    // ───────────── Helper ─────────────

    /// <summary>Item Event thuộc user, gắn connection Calendar; đã setup sẵn repo lookup.</summary>
    private Item SetupEventItem(
        Guid? ownerId = null,
        ItemType type = ItemType.Event,
        Guid? connectionId = null,
        string? externalId = "ev-1",
        string metadataJson = "{}")
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            UserId = ownerId ?? _userId,
            ConnectionId = connectionId ?? _connId,
            Type = type,
            Title = "Standup",
            Snippet = "daily",
            ExternalId = externalId,
            MetadataJson = metadataJson,
            OccurredAt = DateTime.UtcNow
        };

        _itemRepo.Setup(r => r.GetByIdAndUserAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item.UserId == _userId ? item : null);
        _itemRepo.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        return item;
    }

    /// <summary>Connection Calendar thuộc <paramref name="ownerId"/> (mặc định = chủ item).</summary>
    private Connection SetupConnection(Guid? ownerId = null, string providerAccountId = "me@gmail.com")
    {
        var conn = new Connection
        {
            Id = _connId,
            UserId = ownerId ?? _userId,
            ProviderAccountId = providerAccountId,
            ServiceType = ServiceType.GCal,
            Status = ConnectionStatus.Active
        };

        _connectionRepo.Setup(c => c.GetByIdAsync(_connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(conn);

        return conn;
    }

    private static CalendarEvent BuildEvent(
        string id = "ev-1",
        string? etag = "etag-1",
        string? summary = "Standup",
        IReadOnlyList<CalendarEventAttendee>? fullAttendees = null,
        IReadOnlyList<string>? attendees = null,
        bool? guestsCanModify = null,
        bool? guestsCanInviteOthers = null,
        bool? guestsCanSeeOtherGuests = null,
        string? organizerEmail = null)
        => new(
            Id: id,
            ETag: etag,
            Summary: summary,
            Description: "mô tả",
            Start: DateTimeOffset.UtcNow,
            End: DateTimeOffset.UtcNow.AddHours(1),
            Location: "Meet",
            Attendees: attendees,
            FullAttendees: fullAttendees,
            GuestsCanModify: guestsCanModify,
            GuestsCanInviteOthers: guestsCanInviteOthers,
            GuestsCanSeeOtherGuests: guestsCanSeeOtherGuests,
            OrganizerEmail: organizerEmail,
            ICalUid: "uid-1");

    // ───────────── GetTicketAssigneesAsync ─────────────

    [Fact]
    public async Task GetTicketAssigneesAsync_MapsRowsToDtoAndDropsNullAccountId()
    {
        IReadOnlyList<(string? AccountId, string DisplayName)> rows = new List<(string?, string)>
        {
            ("acc-1", "Alice"),
            (null, "Ghost"),              // hàng thiếu accountId → phải bị loại
            ("unassigned", "Chưa gán")
        };

        _itemRepo.Setup(r => r.GetTicketAssigneesAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        var result = await _sut.GetTicketAssigneesAsync(_userId);

        result.Should().HaveCount(2);
        result[0].AccountId.Should().Be("acc-1");
        result[0].DisplayName.Should().Be("Alice");
        result[1].AccountId.Should().Be("unassigned");
        _itemRepo.Verify(r => r.GetTicketAssigneesAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTicketAssigneesAsync_NoTickets_ReturnsEmptyList()
    {
        IReadOnlyList<(string? AccountId, string DisplayName)> rows = new List<(string?, string)>();
        _itemRepo.Setup(r => r.GetTicketAssigneesAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        var result = await _sut.GetTicketAssigneesAsync(_userId);

        result.Should().BeEmpty();
    }

    // ───────────── CreateNoteAsync ─────────────

    [Fact]
    public async Task CreateNoteAsync_WithoutFolder_CreatesNoteItemAndSaves()
    {
        Item? added = null;
        _itemRepo.Setup(r => r.AddAsync(It.IsAny<Item>(), It.IsAny<CancellationToken>()))
            .Callback<Item, CancellationToken>((i, _) => added = i)
            .Returns(Task.CompletedTask);

        var request = new CreateNoteRequest("Ghi chú họp", "Nội dung markdown");

        var result = await _sut.CreateNoteAsync(_userId, request);

        added.Should().NotBeNull();
        added!.Type.Should().Be(ItemType.Note);
        added.UserId.Should().Be(_userId);
        added.ConnectionId.Should().BeNull();
        added.ExternalId.Should().BeNull();
        added.Status.Should().Be(ItemStatus.Inbox);
        added.MetadataJson.Should().Contain("contentMarkdown");
        // System.Text.Json escape ký tự non-ASCII (ộ...) nên phải parse ra rồi mới so chuỗi tiếng Việt.
        System.Text.Json.JsonDocument.Parse(added.MetadataJson!)
            .RootElement.GetProperty("contentMarkdown").GetString()
            .Should().Be("Nội dung markdown");

        result.Id.Should().Be(added.Id);
        result.Title.Should().Be("Ghi chú họp");
        result.Snippet.Should().Be("Nội dung markdown");

        _folderRepo.Verify(f => f.AddItemFolderAsync(It.IsAny<ItemFolder>(), It.IsAny<CancellationToken>()), Times.Never);
        _itemRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateNoteAsync_LongContent_TruncatesSnippetTo200Chars()
    {
        Item? added = null;
        _itemRepo.Setup(r => r.AddAsync(It.IsAny<Item>(), It.IsAny<CancellationToken>()))
            .Callback<Item, CancellationToken>((i, _) => added = i)
            .Returns(Task.CompletedTask);

        var longContent = new string('a', 300);
        var request = new CreateNoteRequest("Ghi chú dài", longContent);

        var result = await _sut.CreateNoteAsync(_userId, request);

        // 197 ký tự + "..." = 200
        result.Snippet.Should().HaveLength(200);
        result.Snippet.Should().EndWith("...");
        // Nội dung đầy đủ vẫn nằm trong metadata, không bị cắt.
        added!.MetadataJson.Should().Contain(longContent);
    }

    [Fact]
    public async Task CreateNoteAsync_WithOwnedFolder_AddsItemFolderAtNextPosition()
    {
        var folderId = Guid.NewGuid();
        _folderRepo.Setup(f => f.ExistsByOwnerAsync(folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _folderRepo.Setup(f => f.GetMaxItemPositionAsync(folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        ItemFolder? link = null;
        _folderRepo.Setup(f => f.AddItemFolderAsync(It.IsAny<ItemFolder>(), It.IsAny<CancellationToken>()))
            .Callback<ItemFolder, CancellationToken>((l, _) => link = l)
            .Returns(Task.CompletedTask);

        var request = new CreateNoteRequest("Ghi chú", "Nội dung", folderId);

        var result = await _sut.CreateNoteAsync(_userId, request);

        link.Should().NotBeNull();
        link!.FolderId.Should().Be(folderId);
        link.ItemId.Should().Be(result.Id);
        link.Position.Should().Be(5);
        _itemRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateNoteAsync_FolderNotOwnedByUser_ThrowsForbidden()
    {
        var folderId = Guid.NewGuid();
        _folderRepo.Setup(f => f.ExistsByOwnerAsync(folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var request = new CreateNoteRequest("Ghi chú", "Nội dung", folderId);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CreateNoteAsync(_userId, request));

        ex.Code.Should().Be(ErrorCodes.FolderOwnerOnly);
        _folderRepo.Verify(f => f.AddItemFolderAsync(It.IsAny<ItemFolder>(), It.IsAny<CancellationToken>()), Times.Never);
        _itemRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ───────────── GetCalendarEventDetailAsync ─────────────

    [Fact]
    public async Task GetCalendarEventDetailAsync_OrganizerOwnEvent_MapsLiveEventToDetailResponse()
    {
        var item = SetupEventItem();
        var conn = SetupConnection();

        var liveEvent = BuildEvent(fullAttendees: new List<CalendarEventAttendee>
        {
            new("me@gmail.com", "Tôi", "accepted", null, Organizer: true),
            new("bob@x.com", "Bob", "needsAction", null, Organizer: false)
        });

        _calendarGateway.Setup(g => g.GetEventAsync(conn, "primary", "ev-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(liveEvent);

        var result = await _sut.GetCalendarEventDetailAsync(_userId, item.Id);

        result.Id.Should().Be(item.Id);
        result.Title.Should().Be("Standup");
        result.OrganizerEmail.Should().Be("me@gmail.com");
        result.OrganizerDisplayName.Should().Be("Tôi");
        result.OwningCalendarName.Should().Be("me@gmail.com");
        result.IsOrganizer.Should().BeTrue();
        result.CanEdit.Should().BeTrue();
        result.CanInviteOthers.Should().BeTrue();
        result.CanSeeGuestList.Should().BeTrue();
        result.Attendees.Should().HaveCount(2);
        result.ICalUid.Should().Be("uid-1");
        result.Reminders.Should().BeEmpty();

        _calendarGateway.Verify(g => g.GetEventAsync(conn, "primary", "ev-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCalendarEventDetailAsync_GuestWithHiddenGuestList_FiltersAttendeesAndBlocksEdit()
    {
        var item = SetupEventItem();
        var conn = SetupConnection();

        // Ta là khách mời, người tổ chức tắt "xem khách khác" và không cho sửa.
        var liveEvent = BuildEvent(
            fullAttendees: new List<CalendarEventAttendee>
            {
                new("alice@x.com", "Alice", "accepted", null, Organizer: true),
                new("me@gmail.com", "Tôi", "needsAction", null, Organizer: false),
                new("bob@x.com", "Bob", "accepted", null, Organizer: false)
            },
            guestsCanModify: false,
            guestsCanInviteOthers: false,
            guestsCanSeeOtherGuests: false);

        _calendarGateway.Setup(g => g.GetEventAsync(conn, "primary", "ev-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(liveEvent);

        var result = await _sut.GetCalendarEventDetailAsync(_userId, item.Id);

        result.IsOrganizer.Should().BeFalse();
        result.CanEdit.Should().BeFalse();
        result.CanInviteOthers.Should().BeFalse();
        result.CanSeeGuestList.Should().BeFalse();
        // Chỉ còn người tổ chức + chính mình.
        result.Attendees.Should().HaveCount(2);
        result.Attendees.Select(a => a.Email).Should().BeEquivalentTo(new[] { "alice@x.com", "me@gmail.com" });
    }

    [Fact]
    public async Task GetCalendarEventDetailAsync_ItemNotFoundAndNotShared_ThrowsNotFound()
    {
        var itemId = Guid.NewGuid();
        _itemRepo.Setup(r => r.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);
        _folderRepo.Setup(f => f.IsItemSharedWithUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => _sut.GetCalendarEventDetailAsync(_userId, itemId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetCalendarEventDetailAsync_ItemIsNotEvent_ThrowsBusinessRule()
    {
        var item = SetupEventItem(type: ItemType.Email);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.GetCalendarEventDetailAsync(_userId, item.Id));

        ex.Code.Should().Be(ErrorCodes.ItemNotCalendarEvent);
    }

    [Fact]
    public async Task GetCalendarEventDetailAsync_EventWithoutConnection_ThrowsBusinessRule()
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            ConnectionId = null,
            Type = ItemType.Event,
            Title = "Standup",
            Snippet = "",
            MetadataJson = "{}"
        };
        _itemRepo.Setup(r => r.GetByIdAndUserAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.GetCalendarEventDetailAsync(_userId, item.Id));

        ex.Code.Should().Be(ErrorCodes.ItemNotLinkedToConnection);
    }

    [Fact]
    public async Task GetCalendarEventDetailAsync_ConnectionNotOwnedByItemOwner_ThrowsForbidden()
    {
        var item = SetupEventItem();
        SetupConnection(ownerId: Guid.NewGuid()); // connection của người khác

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.GetCalendarEventDetailAsync(_userId, item.Id));

        ex.Code.Should().Be(ErrorCodes.NotYourConnection);
        _calendarGateway.Verify(g => g.GetEventAsync(
            It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ───────────── RsvpEventAsync ─────────────

    [Fact]
    public async Task RsvpEventAsync_OwnerAccepts_CallsGatewayAndRefreshesETagAndAttendees()
    {
        var item = SetupEventItem();
        var conn = SetupConnection();

        _calendarGateway.Setup(g => g.GetEventAsync(conn, "primary", "ev-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildEvent(etag: "etag-2", attendees: new List<string> { "bob@x.com" }));

        await _sut.RsvpEventAsync(_userId, item.Id, new RsvpRequest("accepted", "Hẹn gặp"));

        _calendarGateway.Verify(g => g.RsvpEventAsync(
            conn, "primary", "ev-1", "accepted", "Hẹn gặp", It.IsAny<CancellationToken>()), Times.Once);
        item.ETag.Should().Be("etag-2");
        item.MetadataJson.Should().Contain("attendees");
        item.MetadataJson.Should().Contain("bob@x.com");
        _itemRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RsvpEventAsync_SharedEditor_CanRsvpEventOfAnotherUser()
    {
        var ownerId = Guid.NewGuid();
        var item = SetupEventItem(ownerId: ownerId);
        var conn = SetupConnection(ownerId: ownerId);

        _folderRepo.Setup(f => f.IsItemSharedWithUserAsEditorAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _calendarGateway.Setup(g => g.GetEventAsync(conn, "primary", "ev-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildEvent(etag: "etag-3"));

        await _sut.RsvpEventAsync(_userId, item.Id, new RsvpRequest("tentative", null));

        _calendarGateway.Verify(g => g.RsvpEventAsync(
            conn, "primary", "ev-1", "tentative", null, It.IsAny<CancellationToken>()), Times.Once);
        item.ETag.Should().Be("etag-3");
    }

    [Fact]
    public async Task RsvpEventAsync_SharedViewer_ThrowsForbidden()
    {
        var itemId = Guid.NewGuid();
        _itemRepo.Setup(r => r.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);
        _folderRepo.Setup(f => f.IsItemSharedWithUserAsEditorAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _folderRepo.Setup(f => f.IsItemSharedWithUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.RsvpEventAsync(_userId, itemId, new RsvpRequest("accepted", null)));

        ex.Code.Should().Be(ErrorCodes.SharedViewerReadOnly);
    }

    [Fact]
    public async Task RsvpEventAsync_NoAccess_ThrowsNotFound()
    {
        var itemId = Guid.NewGuid();
        _itemRepo.Setup(r => r.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);
        _folderRepo.Setup(f => f.IsItemSharedWithUserAsEditorAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _folderRepo.Setup(f => f.IsItemSharedWithUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => _sut.RsvpEventAsync(_userId, itemId, new RsvpRequest("accepted", null));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task RsvpEventAsync_ItemIsNotEvent_ThrowsBusinessRule()
    {
        var item = SetupEventItem(type: ItemType.Note);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.RsvpEventAsync(_userId, item.Id, new RsvpRequest("accepted", null)));

        ex.Code.Should().Be(ErrorCodes.ItemNotCalendarEvent);
        _calendarGateway.Verify(g => g.RsvpEventAsync(
            It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RsvpEventAsync_ConnectionNotOwnedByItemOwner_ThrowsForbidden()
    {
        var item = SetupEventItem();
        SetupConnection(ownerId: Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.RsvpEventAsync(_userId, item.Id, new RsvpRequest("accepted", null)));

        ex.Code.Should().Be(ErrorCodes.NotYourConnection);
    }

    // ───────────── SendEmailToGuestsAsync ─────────────

    [Fact]
    public async Task SendEmailToGuestsAsync_ValidRequest_SendsViaGmailConnectionWithDistinctRecipients()
    {
        var item = SetupEventItem();

        var gmailConn = new Connection
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            ProviderAccountId = "me@gmail.com",
            ServiceType = ServiceType.Gmail,
            Status = ConnectionStatus.Active
        };
        _connectionRepo.Setup(c => c.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { gmailConn });

        SendEmailRequest? sent = null;
        _sendEmailService.Setup(s => s.SendAsync(_userId, It.IsAny<SendEmailRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, SendEmailRequest, CancellationToken>((_, r, _) => sent = r)
            .ReturnsAsync(new SendEmailResult("msg-1", DateTime.UtcNow));

        var request = new SendEmailToGuestsRequest(
            new List<string> { "bob@x.com", "bob@x.com", "carol@x.com" },
            "Nhắc lịch họp",
            "<p>9h sáng mai</p>",
            SendCopyToMe: true);

        await _sut.SendEmailToGuestsAsync(_userId, item.Id, request);

        sent.Should().NotBeNull();
        sent!.ConnectionId.Should().Be(gmailConn.Id);
        sent.To.Should().BeEquivalentTo(new[] { "bob@x.com", "carol@x.com" });
        sent.Subject.Should().Be("Nhắc lịch họp");
        sent.BodyHtml.Should().Be("<p>9h sáng mai</p>");
        sent.Cc.Should().Contain("me@gmail.com");
    }

    [Fact]
    public async Task SendEmailToGuestsAsync_WithoutSendCopyToMe_LeavesCcEmpty()
    {
        var item = SetupEventItem();

        var gmailConn = new Connection
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            ProviderAccountId = "me@gmail.com",
            ServiceType = ServiceType.Gmail,
            Status = ConnectionStatus.Active
        };
        _connectionRepo.Setup(c => c.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { gmailConn });

        SendEmailRequest? sent = null;
        _sendEmailService.Setup(s => s.SendAsync(_userId, It.IsAny<SendEmailRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, SendEmailRequest, CancellationToken>((_, r, _) => sent = r)
            .ReturnsAsync(new SendEmailResult("msg-1", DateTime.UtcNow));

        var request = new SendEmailToGuestsRequest(
            new List<string> { "bob@x.com" }, "Tiêu đề", "<p>ND</p>", SendCopyToMe: false);

        await _sut.SendEmailToGuestsAsync(_userId, item.Id, request);

        sent!.Cc.Should().BeEmpty();
    }

    [Fact]
    public async Task SendEmailToGuestsAsync_ItemNotFound_ThrowsNotFound()
    {
        var itemId = Guid.NewGuid();
        _itemRepo.Setup(r => r.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);

        var request = new SendEmailToGuestsRequest(new List<string> { "bob@x.com" }, "s", "b", false);

        var act = () => _sut.SendEmailToGuestsAsync(_userId, itemId, request);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SendEmailToGuestsAsync_ItemIsNotEvent_ThrowsBusinessRule()
    {
        var item = SetupEventItem(type: ItemType.File);

        var request = new SendEmailToGuestsRequest(new List<string> { "bob@x.com" }, "s", "b", false);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.SendEmailToGuestsAsync(_userId, item.Id, request));

        ex.Code.Should().Be(ErrorCodes.ItemNotCalendarEvent);
    }

    [Fact]
    public async Task SendEmailToGuestsAsync_NoActiveGmailConnection_ThrowsBusinessRule()
    {
        var item = SetupEventItem();

        // Chỉ có connection Calendar → không gửi được email mời.
        _connectionRepo.Setup(c => c.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    UserId = _userId,
                    ProviderAccountId = "me@gmail.com",
                    ServiceType = ServiceType.GCal,
                    Status = ConnectionStatus.Active
                }
            });

        var request = new SendEmailToGuestsRequest(new List<string> { "bob@x.com" }, "s", "b", false);

        var act = () => _sut.SendEmailToGuestsAsync(_userId, item.Id, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
        _sendEmailService.Verify(s => s.SendAsync(
            It.IsAny<Guid>(), It.IsAny<SendEmailRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendEmailToGuestsAsync_EmptyRecipientList_ThrowsBusinessRule()
    {
        var item = SetupEventItem();

        _connectionRepo.Setup(c => c.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    UserId = _userId,
                    ProviderAccountId = "me@gmail.com",
                    ServiceType = ServiceType.Gmail,
                    Status = ConnectionStatus.Active
                }
            });

        // Toàn chuỗi rỗng → sau khi lọc còn 0 người nhận.
        var request = new SendEmailToGuestsRequest(new List<string> { "", "" }, "s", "b", false);

        var act = () => _sut.SendEmailToGuestsAsync(_userId, item.Id, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
        _sendEmailService.Verify(s => s.SendAsync(
            It.IsAny<Guid>(), It.IsAny<SendEmailRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
