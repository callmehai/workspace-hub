using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Google;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

/// <summary>
/// Test cho ConnectionSyncDispatcher — lớp "định tuyến" sync theo ServiceType.
/// Tập trung vào: routing đúng service, thông báo item mới, và cách xử lý lỗi provider
/// (wrap GoogleApiException → ProviderException, đánh Status=Error khi markProviderError=true).
/// </summary>
public class ConnectionSyncDispatcherTests
{
    private readonly Mock<IConnectionRepository> _connectionsMock = new();
    private readonly Mock<IItemRepository> _itemsMock = new();
    private readonly Mock<ISyncItemNotificationService> _syncNotificationsMock = new();
    private readonly Mock<IGmailSyncService> _gmailSyncMock = new();
    private readonly Mock<ICalendarSyncService> _calendarSyncMock = new();
    private readonly Mock<IDriveSyncService> _driveSyncMock = new();
    private readonly Mock<IJiraSyncService> _jiraSyncMock = new();
    private readonly ConnectionSyncDispatcher _sut;

    public ConnectionSyncDispatcherTests()
    {
        _sut = new ConnectionSyncDispatcher(
            _connectionsMock.Object,
            _itemsMock.Object,
            _syncNotificationsMock.Object,
            _gmailSyncMock.Object,
            _calendarSyncMock.Object,
            _driveSyncMock.Object,
            _jiraSyncMock.Object);

        // Mặc định: chưa có ExternalId nào trước khi sync.
        _itemsMock.Setup(m => m.GetExistingExternalIdsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());
    }

    private Connection SetupConnection(ServiceType serviceType, Guid? userId = null)
    {
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            ServiceType = serviceType,
            Provider = serviceType == ServiceType.Jira ? ProviderType.Atlassian : ProviderType.Google,
            ProviderAccountId = "acc-1",
            Status = ConnectionStatus.Active
        };

        _connectionsMock.Setup(m => m.GetByIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        return connection;
    }

    // ── Guard: connection không tồn tại / không thuộc user ──

    [Fact]
    public async Task SyncAsync_WhenConnectionNotFound_ThrowsNotFoundException()
    {
        var connectionId = Guid.NewGuid();
        _connectionsMock.Setup(m => m.GetByIdAsync(connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        var act = () => _sut.SyncAsync(connectionId, Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SyncAsync_WhenConnectionBelongsToAnotherUser_ThrowsNotFoundException()
    {
        var connection = SetupConnection(ServiceType.Gmail);

        // userId khác chủ sở hữu → coi như không tìm thấy (không lộ tồn tại resource).
        var act = () => _sut.SyncAsync(connection.Id, Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
        _gmailSyncMock.Verify(
            m => m.SyncConnectionAsync(It.IsAny<Connection>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Routing theo ServiceType ──

    [Fact]
    public async Task SyncAsync_WhenGmailConnection_RoutesToGmailSyncService()
    {
        var connection = SetupConnection(ServiceType.Gmail);
        _gmailSyncMock.Setup(m => m.SyncConnectionAsync(connection, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(5, 0, 5, "cursor-gmail"));

        var result = await _sut.SyncAsync(connection.Id, connection.UserId);

        result.NewCursor.Should().Be("cursor-gmail");
        // Batch size mặc định của Gmail = 100.
        _gmailSyncMock.Verify(m => m.SyncConnectionAsync(connection, 100, It.IsAny<CancellationToken>()), Times.Once);
        _calendarSyncMock.Verify(m => m.SyncConnectionAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()), Times.Never);
        _driveSyncMock.Verify(m => m.SyncConnectionAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()), Times.Never);
        _jiraSyncMock.Verify(m => m.SyncConnectionAsync(It.IsAny<Connection>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WhenCalendarConnection_RoutesToCalendarSyncService()
    {
        var connection = SetupConnection(ServiceType.GCal);
        _calendarSyncMock.Setup(m => m.SyncConnectionAsync(connection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(3, 0, 3, "sync-token"));

        var result = await _sut.SyncAsync(connection.Id, connection.UserId);

        result.NewCursor.Should().Be("sync-token");
        _calendarSyncMock.Verify(m => m.SyncConnectionAsync(connection, It.IsAny<CancellationToken>()), Times.Once);
        _gmailSyncMock.Verify(m => m.SyncConnectionAsync(It.IsAny<Connection>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WhenDriveConnection_RoutesToDriveSyncService()
    {
        var connection = SetupConnection(ServiceType.Drive);
        _driveSyncMock.Setup(m => m.SyncConnectionAsync(connection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(7, 0, 7, "page-token"));

        var result = await _sut.SyncAsync(connection.Id, connection.UserId);

        result.NewCursor.Should().Be("page-token");
        _driveSyncMock.Verify(m => m.SyncConnectionAsync(connection, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenJiraConnection_RoutesToJiraSyncServiceWithLargerBatch()
    {
        var connection = SetupConnection(ServiceType.Jira);
        _jiraSyncMock.Setup(m => m.SyncConnectionAsync(connection, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(9, 0, 9, "jql-cursor"));

        var result = await _sut.SyncAsync(connection.Id, connection.UserId);

        result.NewCursor.Should().Be("jql-cursor");
        // Jira kéo cả board → trần 250 để không cụt danh sách ticket.
        _jiraSyncMock.Verify(m => m.SyncConnectionAsync(connection, 250, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenServiceTypeNotSupported_ThrowsBusinessRuleException()
    {
        // Giá trị enum không nằm trong 4 service được hỗ trợ → rơi vào nhánh default.
        var connection = SetupConnection((ServiceType)99);

        var act = () => _sut.SyncAsync(connection.Id, connection.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    // ── Notification item mới ──

    [Fact]
    public async Task SyncAsync_WhenItemsCreated_NotifiesNewItemsWithSnapshotBeforeSync()
    {
        var connection = SetupConnection(ServiceType.Gmail);
        var before = new HashSet<string> { "msg-1", "msg-2" };
        _itemsMock.Setup(m => m.GetExistingExternalIdsAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(before);
        _gmailSyncMock.Setup(m => m.SyncConnectionAsync(connection, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(3, 1, 2, "c"));

        await _sut.SyncAsync(connection.Id, connection.UserId);

        _syncNotificationsMock.Verify(m => m.NotifyNewItemsAsync(
            connection.Id,
            connection.UserId,
            It.Is<IReadOnlySet<string>>(s => s.Count == 2 && s.Contains("msg-1") && s.Contains("msg-2")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenNothingCreated_DoesNotNotify()
    {
        var connection = SetupConnection(ServiceType.GCal);
        _calendarSyncMock.Setup(m => m.SyncConnectionAsync(connection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(4, 0, 4, "c"));

        var result = await _sut.SyncAsync(connection.Id, connection.UserId);

        result.Created.Should().Be(0);
        _syncNotificationsMock.Verify(m => m.NotifyNewItemsAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Lỗi provider ──

    [Fact]
    public async Task SyncAsync_WhenProviderExceptionAndMarkProviderError_SetsConnectionStatusErrorAndRethrows()
    {
        var connection = SetupConnection(ServiceType.Jira);
        var tracked = new Connection
        {
            Id = connection.Id,
            UserId = connection.UserId,
            ServiceType = ServiceType.Jira,
            ProviderAccountId = "acc-1",
            Status = ConnectionStatus.Active
        };
        _connectionsMock.Setup(m => m.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tracked);
        _jiraSyncMock.Setup(m => m.SyncConnectionAsync(connection, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderException("Jira 502", HttpStatusCode.BadGateway));

        var ex = await Assert.ThrowsAsync<ProviderException>(
            async () => await _sut.SyncAsync(connection.Id, connection.UserId, CancellationToken.None, markProviderError: true));

        ex.Message.Should().Be("Jira 502");
        tracked.Status.Should().Be(ConnectionStatus.Error);
        tracked.LastError.Should().Be("Jira 502");
        _connectionsMock.Verify(m => m.Update(tracked), Times.Once);
        _connectionsMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenProviderExceptionAndNotMarkProviderError_DoesNotTouchConnection()
    {
        var connection = SetupConnection(ServiceType.Jira);
        _jiraSyncMock.Setup(m => m.SyncConnectionAsync(connection, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderException("Jira 502", HttpStatusCode.BadGateway));

        // markProviderError mặc định = false (luồng cron/on-demand tự phân loại lỗi).
        var act = () => _sut.SyncAsync(connection.Id, connection.UserId);

        await act.Should().ThrowAsync<ProviderException>();

        _connectionsMock.Verify(m => m.GetByIdTrackedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _connectionsMock.Verify(m => m.Update(It.IsAny<Connection>()), Times.Never);
        _connectionsMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WhenGoogleApiException_WrapsIntoProviderExceptionWithStatusCode()
    {
        var connection = SetupConnection(ServiceType.Gmail);
        _gmailSyncMock.Setup(m => m.SyncConnectionAsync(connection, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleApiException("gmail", "insufficient scope")
            {
                HttpStatusCode = HttpStatusCode.Forbidden
            });

        var ex = await Assert.ThrowsAsync<ProviderException>(
            async () => await _sut.SyncAsync(connection.Id, connection.UserId));

        ex.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ex.Message.Should().Contain("403");
    }

    [Fact]
    public async Task SyncAsync_WhenGoogleApiExceptionAndMarkProviderError_MarksConnectionError()
    {
        var connection = SetupConnection(ServiceType.Drive);
        var tracked = new Connection
        {
            Id = connection.Id,
            UserId = connection.UserId,
            ServiceType = ServiceType.Drive,
            ProviderAccountId = "acc-1",
            Status = ConnectionStatus.Active
        };
        _connectionsMock.Setup(m => m.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tracked);
        _driveSyncMock.Setup(m => m.SyncConnectionAsync(connection, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleApiException("drive", "rate limit")
            {
                HttpStatusCode = HttpStatusCode.TooManyRequests
            });

        var act = () => _sut.SyncAsync(connection.Id, connection.UserId, CancellationToken.None, markProviderError: true);

        await act.Should().ThrowAsync<ProviderException>();

        tracked.Status.Should().Be(ConnectionStatus.Error);
        tracked.LastError.Should().Contain("429");
        _connectionsMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenMarkProviderErrorButTrackedConnectionMissing_StillRethrowsWithoutSaving()
    {
        var connection = SetupConnection(ServiceType.Jira);
        _connectionsMock.Setup(m => m.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);
        _jiraSyncMock.Setup(m => m.SyncConnectionAsync(connection, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderException("Jira down"));

        var act = () => _sut.SyncAsync(connection.Id, connection.UserId, CancellationToken.None, markProviderError: true);

        await act.Should().ThrowAsync<ProviderException>();
        _connectionsMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WhenSyncSucceeds_DoesNotMarkConnectionError()
    {
        var connection = SetupConnection(ServiceType.GCal);
        _calendarSyncMock.Setup(m => m.SyncConnectionAsync(connection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(2, 2, 0, "tok"));

        var result = await _sut.SyncAsync(connection.Id, connection.UserId, CancellationToken.None, markProviderError: true);

        result.Created.Should().Be(2);
        result.Scanned.Should().Be(2);
        // Dispatcher không tự đụng vào Connection ở happy path — service con lo cursor/LastSyncedAt.
        _connectionsMock.Verify(m => m.GetByIdTrackedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _connectionsMock.Verify(m => m.Update(It.IsAny<Connection>()), Times.Never);
    }
}
