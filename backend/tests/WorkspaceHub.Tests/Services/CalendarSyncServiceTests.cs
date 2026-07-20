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
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

/// <summary>
/// Test cho CalendarSyncService — sync event Google Calendar về Item(Type=Event).
/// Bao phủ: map event → Item, đếm scanned/created/skipped, dedupe theo ExternalId,
/// syncToken (kể cả khi hết hạn → full sync), xoá event bị cancel, reconcile invitation,
/// và cập nhật cursor/trạng thái Connection.
/// </summary>
public class CalendarSyncServiceTests
{
    private readonly Mock<ICalendarGateway> _gatewayMock = new();
    private readonly Mock<ICalendarItemMapper> _mapperMock = new();
    private readonly Mock<IItemRepository> _itemsMock = new();
    private readonly Mock<IConnectionRepository> _connectionsMock = new();
    private readonly Mock<ICalendarInvitationService> _invitationsMock = new();
    private readonly Mock<ILogger<CalendarSyncService>> _loggerMock = new();
    private readonly CalendarSyncService _sut;

    public CalendarSyncServiceTests()
    {
        _sut = new CalendarSyncService(
            _gatewayMock.Object,
            _mapperMock.Object,
            _itemsMock.Object,
            _connectionsMock.Object,
            _invitationsMock.Object,
            _loggerMock.Object);

        // Mặc định: chưa có item nào local, không có file Drive nào khớp attachment.
        _itemsMock.Setup(m => m.GetTrackedByConnectionIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item>());
        _itemsMock.Setup(m => m.GetFilesByExternalIdsAsync(
                It.IsAny<Guid>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Item>());
        _itemsMock.Setup(m => m.AddRangeAsync(It.IsAny<IEnumerable<Item>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Mapper giả: giữ nguyên các field cần thiết để test dedupe/update.
        _mapperMock.Setup(m => m.ToItem(
                It.IsAny<CalendarEventDto>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyDictionary<string, Guid>?>()))
            .Returns((CalendarEventDto ev, Guid userId, Guid connId, IReadOnlyDictionary<string, Guid>? _) => new Item
            {
                Id = Guid.NewGuid(),
                ExternalId = ev.Id,
                UserId = userId,
                ConnectionId = connId,
                Type = ItemType.Event,
                Title = ev.Title,
                Snippet = ev.Snippet,
                ETag = ev.ETag,
                MetadataJson = "{}",
                OccurredAt = ev.Start?.UtcDateTime ?? DateTime.UtcNow,
                DueAt = ev.End?.UtcDateTime
            });
    }

    private static Connection CalendarConnection(string? cursor = null, CursorType? cursorType = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Provider = ProviderType.Google,
            ServiceType = ServiceType.GCal,
            ProviderAccountId = "owner@example.com",
            CursorValue = cursor,
            CursorType = cursorType,
            // Cố tình để Error + LastError để kiểm tra sync thành công sẽ reset lại.
            Status = ConnectionStatus.Error,
            LastError = "lỗi cũ"
        };

    private static CalendarEventDto Event(string id, string? etag = null) => new()
    {
        Id = id,
        ETag = etag ?? $"etag-{id}",
        Title = $"Event {id}",
        Snippet = $"snippet {id}",
        Start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
        End = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero)
    };

    private void SetupGateway(Connection conn, CalendarSyncResult result, string? syncToken = null)
        => _gatewayMock.Setup(m => m.SyncEventsAsync(conn, syncToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private List<Item> CaptureAddedItems()
    {
        var captured = new List<Item>();
        _itemsMock.Setup(m => m.AddRangeAsync(It.IsAny<IEnumerable<Item>>(), It.IsAny<CancellationToken>()))
            .Callback((IEnumerable<Item> items, CancellationToken _) => captured.AddRange(items))
            .Returns(Task.CompletedTask);
        return captured;
    }

    // ── Tạo mới ──

    [Fact]
    public async Task SyncConnectionAsync_WhenAllEventsNew_CreatesItemsAndUpdatesCursor()
    {
        var conn = CalendarConnection();
        SetupGateway(conn, new CalendarSyncResult(false, new List<CalendarEventDto> { Event("ev1"), Event("ev2") }, "token-next"));

        var added = CaptureAddedItems();

        var result = await _sut.SyncConnectionAsync(conn);

        result.Scanned.Should().Be(2);
        result.Created.Should().Be(2);
        result.Skipped.Should().Be(0);
        result.NewCursor.Should().Be("token-next");

        added.Should().HaveCount(2);
        added.Select(i => i.ExternalId).Should().BeEquivalentTo(new[] { "ev1", "ev2" });
        added.Should().OnlyContain(i => i.UserId == conn.UserId && i.ConnectionId == conn.Id);

        conn.CursorType.Should().Be(CursorType.SyncToken);
        conn.CursorValue.Should().Be("token-next");
        conn.Status.Should().Be(ConnectionStatus.Active);
        conn.LastError.Should().BeNull();
        conn.LastSyncedAt.Should().NotBeNull();

        _itemsMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _connectionsMock.Verify(m => m.Update(conn), Times.Once);
        _connectionsMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncConnectionAsync_WhenNoEvents_DoesNotAddRangeButStillUpdatesConnection()
    {
        var conn = CalendarConnection();
        SetupGateway(conn, new CalendarSyncResult(false, new List<CalendarEventDto>(), "token-empty"));

        var result = await _sut.SyncConnectionAsync(conn);

        result.Scanned.Should().Be(0);
        result.Created.Should().Be(0);
        result.Skipped.Should().Be(0);

        _itemsMock.Verify(m => m.AddRangeAsync(It.IsAny<IEnumerable<Item>>(), It.IsAny<CancellationToken>()), Times.Never);
        _itemsMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        conn.CursorValue.Should().Be("token-empty");
        conn.LastSyncedAt.Should().NotBeNull();
        _connectionsMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Item đã tồn tại (dedupe theo ExternalId) ──

    [Fact]
    public async Task SyncConnectionAsync_WhenEventExistsWithSameETag_SkipsWithoutUpdating()
    {
        var conn = CalendarConnection();
        var existing = new Item
        {
            Id = Guid.NewGuid(),
            ExternalId = "ev1",
            Title = "tiêu đề cũ",
            Snippet = "",
            ETag = "etag-ev1",
            MetadataJson = "{}",
            Status = ItemStatus.Done
        };
        _itemsMock.Setup(m => m.GetTrackedByConnectionIdAsync(conn.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item> { ["ev1"] = existing });

        SetupGateway(conn, new CalendarSyncResult(false, new List<CalendarEventDto> { Event("ev1") }, "tok"));

        var result = await _sut.SyncConnectionAsync(conn);

        result.Scanned.Should().Be(1);
        result.Created.Should().Be(0);
        result.Skipped.Should().Be(1);

        // ETag không đổi và metadata không có attachment mới → không ghi đè gì.
        existing.Title.Should().Be("tiêu đề cũ");
        _itemsMock.Verify(m => m.AddRangeAsync(It.IsAny<IEnumerable<Item>>(), It.IsAny<CancellationToken>()), Times.Never);
        _itemsMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncConnectionAsync_WhenEventExistsWithNewETag_UpdatesFieldsButKeepsKanbanStatus()
    {
        var conn = CalendarConnection();
        var existing = new Item
        {
            Id = Guid.NewGuid(),
            ExternalId = "ev1",
            Title = "tiêu đề cũ",
            Snippet = "cũ",
            ETag = "etag-cũ",
            MetadataJson = "{}",
            Status = ItemStatus.Doing
        };
        _itemsMock.Setup(m => m.GetTrackedByConnectionIdAsync(conn.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item> { ["ev1"] = existing });

        SetupGateway(conn, new CalendarSyncResult(false, new List<CalendarEventDto> { Event("ev1") }, "tok"));

        var result = await _sut.SyncConnectionAsync(conn);

        result.Created.Should().Be(0);
        result.Skipped.Should().Be(1);

        existing.Title.Should().Be("Event ev1");
        existing.Snippet.Should().Be("snippet ev1");
        existing.ETag.Should().Be("etag-ev1");
        // Không reset cột Kanban khi sync lại.
        existing.Status.Should().Be(ItemStatus.Doing);

        _itemsMock.Verify(m => m.AddRangeAsync(It.IsAny<IEnumerable<Item>>(), It.IsAny<CancellationToken>()), Times.Never);
        _itemsMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── syncToken / cursor ──

    [Fact]
    public async Task SyncConnectionAsync_WhenCursorTypeIsSyncToken_SendsCursorValueToGateway()
    {
        var conn = CalendarConnection("token-cũ", CursorType.SyncToken);
        _gatewayMock.Setup(m => m.SyncEventsAsync(conn, "token-cũ", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarSyncResult(false, new List<CalendarEventDto>(), "token-mới"));

        var result = await _sut.SyncConnectionAsync(conn);

        _gatewayMock.Verify(m => m.SyncEventsAsync(conn, "token-cũ", It.IsAny<CancellationToken>()), Times.Once);
        result.NewCursor.Should().Be("token-mới");
        conn.CursorValue.Should().Be("token-mới");
    }

    [Fact]
    public async Task SyncConnectionAsync_WhenCursorTypeIsNotSyncToken_SendsNullTokenToGateway()
    {
        // Cursor còn sót từ provider khác (vd PageToken) → không dùng làm syncToken.
        var conn = CalendarConnection("page-token", CursorType.PageToken);
        SetupGateway(conn, new CalendarSyncResult(false, new List<CalendarEventDto>(), "token-mới"));

        await _sut.SyncConnectionAsync(conn);

        _gatewayMock.Verify(m => m.SyncEventsAsync(conn, null, It.IsAny<CancellationToken>()), Times.Once);
        _gatewayMock.Verify(m => m.SyncEventsAsync(conn, "page-token", It.IsAny<CancellationToken>()), Times.Never);
        conn.CursorType.Should().Be(CursorType.SyncToken);
    }

    [Fact]
    public async Task SyncConnectionAsync_WhenSyncTokenExpired_FallsBackToFullSync()
    {
        var conn = CalendarConnection("token-hết-hạn", CursorType.SyncToken);

        _gatewayMock.Setup(m => m.SyncEventsAsync(conn, "token-hết-hạn", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarSyncResult(true, new List<CalendarEventDto>(), null));
        _gatewayMock.Setup(m => m.SyncEventsAsync(conn, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarSyncResult(false, new List<CalendarEventDto> { Event("ev1") }, "token-full"));

        var result = await _sut.SyncConnectionAsync(conn);

        _gatewayMock.Verify(m => m.SyncEventsAsync(conn, null, It.IsAny<CancellationToken>()), Times.Once);
        result.Created.Should().Be(1);
        result.NewCursor.Should().Be("token-full");
        conn.CursorValue.Should().Be("token-full");
    }

    // ── Event bị huỷ trên Google ──

    [Fact]
    public async Task SyncConnectionAsync_WhenEventsCancelled_ClearsInviteeLinksAndRemovesItems()
    {
        var conn = CalendarConnection();
        var cancelledItem = new Item
        {
            Id = Guid.NewGuid(), ExternalId = "ev-huỷ", Title = "sẽ bị xoá",
            Snippet = "", MetadataJson = "{}"
        };
        _itemsMock.Setup(m => m.GetTrackedByConnectionIdAsync(conn.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item> { ["ev-huỷ"] = cancelledItem });

        SetupGateway(conn, new CalendarSyncResult(
            false,
            new List<CalendarEventDto>(),
            "tok",
            new List<string> { "ev-huỷ" }));

        var result = await _sut.SyncConnectionAsync(conn);

        result.Scanned.Should().Be(0);
        _invitationsMock.Verify(m => m.ClearInviteeItemLinksAsync(
            It.Is<IEnumerable<Guid>>(ids => ids.Contains(cancelledItem.Id)), It.IsAny<CancellationToken>()), Times.Once);
        _itemsMock.Verify(m => m.Remove(cancelledItem), Times.Once);
        _itemsMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncConnectionAsync_WhenCancelledIdHasNoLocalItem_DoesNotRemoveAnything()
    {
        var conn = CalendarConnection();
        SetupGateway(conn, new CalendarSyncResult(
            false,
            new List<CalendarEventDto>(),
            "tok",
            new List<string> { "không-tồn-tại" }));

        await _sut.SyncConnectionAsync(conn);

        _itemsMock.Verify(m => m.Remove(It.IsAny<Item>()), Times.Never);
        _invitationsMock.Verify(m => m.ClearInviteeItemLinksAsync(
            It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Invitation reconcile ──

    [Fact]
    public async Task SyncConnectionAsync_CallsInvitationReconcileForEveryEvent()
    {
        var conn = CalendarConnection();
        var existing = new Item
        {
            Id = Guid.NewGuid(), ExternalId = "ev-cũ", Title = "cũ",
            Snippet = "", ETag = "etag-ev-cũ", MetadataJson = "{}"
        };
        _itemsMock.Setup(m => m.GetTrackedByConnectionIdAsync(conn.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item> { ["ev-cũ"] = existing });

        SetupGateway(conn, new CalendarSyncResult(
            false, new List<CalendarEventDto> { Event("ev-cũ"), Event("ev-mới") }, "tok"));

        await _sut.SyncConnectionAsync(conn);

        // Cả item mới lẫn item đã tồn tại đều được reconcile lời mời.
        _invitationsMock.Verify(m => m.ReconcileSyncedEventAsync(
            conn, It.IsAny<Item>(), It.IsAny<CalendarEventDto>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _invitationsMock.Verify(m => m.ReconcileSyncedEventAsync(
            conn, existing, It.Is<CalendarEventDto>(e => e.Id == "ev-cũ"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncConnectionAsync_WhenReconcileThrows_StillPersistsCursor()
    {
        var conn = CalendarConnection();
        SetupGateway(conn, new CalendarSyncResult(false, new List<CalendarEventDto> { Event("ev1") }, "token-next"));

        _invitationsMock.Setup(m => m.ReconcileSyncedEventAsync(
                It.IsAny<Connection>(), It.IsAny<Item>(), It.IsAny<CalendarEventDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RSVP tới event đã bị xoá"));

        // Reconcile là best-effort: lỗi không được chặn việc lưu syncToken (nếu không cursor kẹt mãi).
        var result = await _sut.SyncConnectionAsync(conn);

        result.Created.Should().Be(1);
        conn.CursorValue.Should().Be("token-next");
        conn.Status.Should().Be(ConnectionStatus.Active);
        _connectionsMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Drive attachment lookup ──

    [Fact]
    public async Task SyncConnectionAsync_WithDriveAttachments_PassesDeduplicatedFileLookupToMapper()
    {
        var conn = CalendarConnection();
        var ev = Event("ev1");
        ev.DriveAttachments.Add(new CalendarDriveAttachment("file-1", "Tài liệu", "application/pdf", "https://drive/x"));

        SetupGateway(conn, new CalendarSyncResult(false, new List<CalendarEventDto> { ev }, "tok"));

        // Cùng 1 file Drive có thể tồn tại ở nhiều Item → service phải group tránh trùng key.
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        _itemsMock.Setup(m => m.GetFilesByExternalIdsAsync(
                conn.UserId, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Item>
            {
                new() { Id = idA, ExternalId = "file-1", Type = ItemType.File, Title = "f", Snippet = "", MetadataJson = "{}" },
                new() { Id = idB, ExternalId = "file-1", Type = ItemType.File, Title = "f", Snippet = "", MetadataJson = "{}" }
            });

        IReadOnlyDictionary<string, Guid>? capturedLookup = null;
        _mapperMock.Setup(m => m.ToItem(
                It.IsAny<CalendarEventDto>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyDictionary<string, Guid>?>()))
            .Callback((CalendarEventDto _, Guid _, Guid _, IReadOnlyDictionary<string, Guid>? lookup) => capturedLookup = lookup)
            .Returns((CalendarEventDto e, Guid userId, Guid connId, IReadOnlyDictionary<string, Guid>? _) => new Item
            {
                Id = Guid.NewGuid(), ExternalId = e.Id, UserId = userId, ConnectionId = connId,
                Type = ItemType.Event, Title = e.Title, Snippet = e.Snippet, ETag = e.ETag, MetadataJson = "{}"
            });

        await _sut.SyncConnectionAsync(conn);

        capturedLookup.Should().NotBeNull();
        capturedLookup!.Should().ContainKey("file-1");
        capturedLookup["file-1"].Should().Be(new[] { idA, idB }.OrderBy(x => x).First());
    }

    [Fact]
    public async Task SyncConnectionAsync_WhenNoAttachments_DoesNotQueryDriveFiles()
    {
        var conn = CalendarConnection();
        SetupGateway(conn, new CalendarSyncResult(false, new List<CalendarEventDto> { Event("ev1") }, "tok"));

        await _sut.SyncConnectionAsync(conn);

        _itemsMock.Verify(m => m.GetFilesByExternalIdsAsync(
            It.IsAny<Guid>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Reminder ──

    [Fact]
    public async Task SyncConnectionAsync_MapsGoogleRemindersToLocalReminders()
    {
        var conn = CalendarConnection();
        var ev = Event("ev1");
        ev.AllDay = false;
        ev.Reminders.Add(new CalendarEventReminder("popup", 30));
        ev.Reminders.Add(new CalendarEventReminder("email", 10));

        SetupGateway(conn, new CalendarSyncResult(false, new List<CalendarEventDto> { ev }, "tok"));

        var added = CaptureAddedItems();

        await _sut.SyncConnectionAsync(conn);

        added.Should().HaveCount(1);
        var reminders = added[0].Reminders.ToList();
        reminders.Should().HaveCount(2);
        reminders.Should().Contain(r =>
            r.ReminderType == ReminderType.GooglePopup && r.OffsetValue == 30 && r.OffsetUnit == ReminderUnit.Minutes);
        reminders.Should().Contain(r =>
            r.ReminderType == ReminderType.GoogleEmail && r.OffsetValue == 10 && r.OffsetUnit == ReminderUnit.Minutes);
        reminders.Should().OnlyContain(r => !r.IsSent);
    }

    // ── Lỗi provider ──

    [Fact]
    public async Task SyncConnectionAsync_WhenGatewayThrowsProviderException_Propagates()
    {
        var conn = CalendarConnection();
        _gatewayMock.Setup(m => m.SyncEventsAsync(conn, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderException("Calendar API lỗi", System.Net.HttpStatusCode.BadGateway));

        var act = () => _sut.SyncConnectionAsync(conn);

        await act.Should().ThrowAsync<ProviderException>();

        // Lỗi giữa chừng → không được lưu cursor/LastSyncedAt.
        conn.LastSyncedAt.Should().BeNull();
        _connectionsMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
