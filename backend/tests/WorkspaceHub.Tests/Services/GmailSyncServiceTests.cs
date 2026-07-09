using FluentAssertions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Microsoft.Extensions.Logging;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class GmailSyncServiceTests
{
    private readonly Mock<IGmailGateway> _gatewayMock;
    private readonly Mock<IGmailItemMapper> _mapperMock;
    private readonly Mock<IItemRepository> _itemsMock;
    private readonly Mock<IImportantContactRepository> _importantContactsMock;
    private readonly Mock<IConnectionRepository> _connectionsMock;
    private readonly Mock<ITokenService> _tokenServiceMock;
    private readonly Mock<IPeopleGateway> _peopleMock;
    private readonly Mock<IGoogleContactRepository> _googleContactsMock;
    private readonly Mock<IGoogleContactMapper> _googleContactMapperMock;
    private readonly Mock<ILogger<GmailSyncService>> _loggerMock;
    private readonly GmailSyncService _service;

    public GmailSyncServiceTests()
    {
        _gatewayMock = new Mock<IGmailGateway>();
        _mapperMock = new Mock<IGmailItemMapper>();
        _itemsMock = new Mock<IItemRepository>();
        _importantContactsMock = new Mock<IImportantContactRepository>();
        _connectionsMock = new Mock<IConnectionRepository>();
        _tokenServiceMock = new Mock<ITokenService>();
        _peopleMock = new Mock<IPeopleGateway>();
        _googleContactsMock = new Mock<IGoogleContactRepository>();
        _googleContactMapperMock = new Mock<IGoogleContactMapper>();
        _loggerMock = new Mock<ILogger<GmailSyncService>>();

        _peopleMock.Setup(m => m.ListAllAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PeopleContactRow>());
        _googleContactsMock.Setup(m => m.ReplaceAllForConnectionAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<GoogleContact>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _googleContactMapperMock.Setup(m => m.ToEntity(It.IsAny<PeopleContactRow>(), It.IsAny<Guid>(), It.IsAny<DateTime>()))
            .Returns((PeopleContactRow row, Guid connId, DateTime syncedAt) => new GoogleContact
            {
                Id = Guid.NewGuid(),
                ConnectionId = connId,
                Email = row.Email,
                DisplayName = row.DisplayName,
                Source = row.Source,
                ExternalResourceName = row.ExternalResourceName,
                SyncedAt = syncedAt
            });

        _service = new GmailSyncService(
            _gatewayMock.Object,
            _mapperMock.Object,
            _itemsMock.Object,
            _importantContactsMock.Object,
            _connectionsMock.Object,
            _tokenServiceMock.Object,
            _peopleMock.Object,
            _googleContactsMock.Object,
            _googleContactMapperMock.Object,
            _loggerMock.Object);

        _importantContactsMock.Setup(m => m.GetIdentifiersAsync(It.IsAny<Guid>(), It.IsAny<ImportantContactType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
    }

    [Fact]
    public async Task FullSync_WhenCursorNull_CreatesAllItems()
    {
        var connection = new Connection { Id = Guid.NewGuid(), UserId = Guid.NewGuid() };
        
        _gatewayMock.Setup(m => m.GetProfileAsync(connection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailProfile("a", 100, 10, 10));

        _gatewayMock.Setup(m => m.ListMessageIdsAsync(connection, null, It.IsAny<int>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailMessageList(new List<string> { "1", "2", "3" }, null));

        _itemsMock.Setup(m => m.GetTrackedByConnectionIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item>());

        _gatewayMock.Setup(m => m.GetMessageAsync(connection, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection c, string id, CancellationToken ct) => 
                new GmailMessage(id, "t", "S", null, new List<string>(), new List<string>(), new List<string>(), null, new List<string>(), false, null));

        _mapperMock.Setup(m => m.ToItem(It.IsAny<GmailMessage>(), connection.UserId, connection.Id, It.IsAny<ISet<string>>()))
            .Returns((GmailMessage msg, Guid u, Guid c, ISet<string> s) => new Item { ExternalId = msg.Id });

        var result = await _service.SyncConnectionAsync(connection);

        result.Created.Should().Be(3);
        result.Skipped.Should().Be(0);
        result.NewCursor.Should().Be("100");
        
        connection.CursorValue.Should().Be("100");
        connection.CursorType.Should().Be(CursorType.HistoryId);
        connection.Status.Should().Be(ConnectionStatus.Active);
        connection.LastSyncedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task FullSync_WithExistingIds_DedupsCorrectly()
    {
        var connection = new Connection { Id = Guid.NewGuid(), UserId = Guid.NewGuid() };
        
        _gatewayMock.Setup(m => m.GetProfileAsync(connection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailProfile("a", 100, 10, 10));

        _gatewayMock.Setup(m => m.ListMessageIdsAsync(connection, null, It.IsAny<int>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailMessageList(new List<string> { "1", "2", "3" }, null));

        _itemsMock.Setup(m => m.GetTrackedByConnectionIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item> { { "2", new Item { ExternalId = "2", ETag = "old" } } });

        _gatewayMock.Setup(m => m.GetMessageAsync(connection, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection c, string id, CancellationToken ct) => 
                new GmailMessage(id, "t", "S", null, new List<string>(), new List<string>(), new List<string>(), null, new List<string>(), false, null));

        _mapperMock.Setup(m => m.ToItem(It.IsAny<GmailMessage>(), connection.UserId, connection.Id, It.IsAny<ISet<string>>()))
            .Returns((GmailMessage msg, Guid u, Guid c, ISet<string> s) => new Item { ExternalId = msg.Id, ETag = "new" });

        var result = await _service.SyncConnectionAsync(connection);

        // Discovery (listing) chỉ fetch id CHƯA có → id "2" đã tồn tại bị bỏ QUA TRƯỚC khi fetch (không
        // tính "skipped" nữa, để nhẹ). Chỉ "1","3" được fetch → Created=2, Skipped=0.
        result.Created.Should().Be(2);
        result.Skipped.Should().Be(0);
    }

    [Fact]
    public async Task IncrementalSync_WhenNotExpired_CallsHistoryAndCreatesItems()
    {
        var connection = new Connection { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), CursorValue = "50", CursorType = CursorType.HistoryId };

        _itemsMock.Setup(m => m.GetTrackedByConnectionIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item>());

        _gatewayMock.Setup(m => m.ListHistoryAsync(connection, "50", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailHistory(false, new List<string> { "m1", "m2" }, null, "120"));

        // Incremental giờ VẪN quét recent các hộp thư (spam/trash/…) — trả rỗng ở test này.
        _gatewayMock.Setup(m => m.ListMessageIdsAsync(It.IsAny<Connection>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailMessageList(new List<string>(), null));

        _gatewayMock.Setup(m => m.GetMessageAsync(connection, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection c, string id, CancellationToken ct) =>
                new GmailMessage(id, "t", "S", null, new List<string>(), new List<string>(), new List<string>(), null, new List<string>(), false, null));

        _mapperMock.Setup(m => m.ToItem(It.IsAny<GmailMessage>(), connection.UserId, connection.Id, It.IsAny<ISet<string>>()))
            .Returns((GmailMessage msg, Guid u, Guid c, ISet<string> s) => new Item { ExternalId = msg.Id });

        var result = await _service.SyncConnectionAsync(connection);

        _gatewayMock.Verify(m => m.ListHistoryAsync(connection, "50", null, It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        result.Created.Should().Be(2);
        result.NewCursor.Should().Be("120");
        connection.CursorValue.Should().Be("120");
    }

    [Fact]
    public async Task IncrementalSync_WhenExpired_FallsBackToFullSync()
    {
        var connection = new Connection { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), CursorValue = "50", CursorType = CursorType.HistoryId };

        _itemsMock.Setup(m => m.GetTrackedByConnectionIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item>());

        _gatewayMock.Setup(m => m.ListHistoryAsync(connection, "50", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailHistory(true, new List<string>(), null, null));

        _gatewayMock.Setup(m => m.GetProfileAsync(connection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailProfile("a", 200, 10, 10));

        _gatewayMock.Setup(m => m.ListMessageIdsAsync(connection, null, It.IsAny<int>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailMessageList(new List<string> { "f1" }, null));

        _gatewayMock.Setup(m => m.GetMessageAsync(connection, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection c, string id, CancellationToken ct) => 
                new GmailMessage(id, "t", "S", null, new List<string>(), new List<string>(), new List<string>(), null, new List<string>(), false, null));

        _mapperMock.Setup(m => m.ToItem(It.IsAny<GmailMessage>(), connection.UserId, connection.Id, It.IsAny<ISet<string>>()))
            .Returns((GmailMessage msg, Guid u, Guid c, ISet<string> s) => new Item { ExternalId = msg.Id });

        var result = await _service.SyncConnectionAsync(connection);

        // FullSync giờ list global + từng mailbox (SENT/DRAFT/STARRED/CATEGORY_*) → nhiều lần.
        _gatewayMock.Verify(m => m.ListMessageIdsAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        result.Created.Should().Be(1);
        result.NewCursor.Should().Be("200");
        connection.CursorValue.Should().Be("200");
    }

    [Fact]
    public async Task FullSync_WhenEmpty_DoesNotCallAddRangeButUpdatesCursor()
    {
        var connection = new Connection { Id = Guid.NewGuid(), UserId = Guid.NewGuid() };
        
        _gatewayMock.Setup(m => m.GetProfileAsync(connection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailProfile("a", 100, 10, 10));

        _gatewayMock.Setup(m => m.ListMessageIdsAsync(connection, null, It.IsAny<int>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailMessageList(new List<string>(), null));

        _itemsMock.Setup(m => m.GetTrackedByConnectionIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item>());

        var result = await _service.SyncConnectionAsync(connection);

        result.Created.Should().Be(0);
        
        connection.CursorValue.Should().Be("100");
        connection.LastSyncedAt.Should().NotBeNull();
        _connectionsMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Sync_ReplacesContactsFromBothSources()
    {
        var connection = new Connection { Id = Guid.NewGuid(), UserId = Guid.NewGuid() };

        _gatewayMock.Setup(m => m.GetProfileAsync(connection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailProfile("a", 100, 10, 10));
        _gatewayMock.Setup(m => m.ListMessageIdsAsync(connection, null, It.IsAny<int>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailMessageList(new List<string>(), null));
        _itemsMock.Setup(m => m.GetTrackedByConnectionIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item>());

        _peopleMock.Setup(m => m.ListAllAsync(connection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PeopleContactRow>
            {
                new() { Email = "alice@example.com", DisplayName = "Alice", Source = GoogleContactSource.Contact, ExternalResourceName = "people/c1" },
                new() { Email = "bob@example.com", DisplayName = "Bob", Source = GoogleContactSource.OtherContact, ExternalResourceName = "people/c2" },
            });

        await _service.SyncConnectionAsync(connection);

        _googleContactsMock.Verify(m => m.ReplaceAllForConnectionAsync(
            connection.Id,
            It.Is<IReadOnlyList<GoogleContact>>(list =>
                list.Count == 2 &&
                list.Any(c => c.Email == "alice@example.com" && c.Source == GoogleContactSource.Contact) &&
                list.Any(c => c.Email == "bob@example.com" && c.Source == GoogleContactSource.OtherContact)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Sync_WhenContactSyncFails_StillCompletesMailSync()
    {
        var connection = new Connection { Id = Guid.NewGuid(), UserId = Guid.NewGuid() };

        _gatewayMock.Setup(m => m.GetProfileAsync(connection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailProfile("a", 100, 10, 10));
        _gatewayMock.Setup(m => m.ListMessageIdsAsync(connection, null, It.IsAny<int>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailMessageList(new List<string>(), null));
        _itemsMock.Setup(m => m.GetTrackedByConnectionIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item>());
        _peopleMock.Setup(m => m.ListAllAsync(connection, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("People API down"));

        var result = await _service.SyncConnectionAsync(connection);

        result.NewCursor.Should().Be("100");
        connection.Status.Should().Be(ConnectionStatus.Active);
        _googleContactsMock.Verify(m => m.ReplaceAllForConnectionAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<GoogleContact>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
