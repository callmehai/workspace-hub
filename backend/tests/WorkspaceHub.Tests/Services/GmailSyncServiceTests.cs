using FluentAssertions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class GmailSyncServiceTests
{
    private readonly Mock<IGmailGateway> _gatewayMock;
    private readonly Mock<IGmailItemMapper> _mapperMock;
    private readonly Mock<IItemRepository> _itemsMock;
    private readonly Mock<IImportantContactRepository> _importantContactsMock;
    private readonly Mock<IConnectionRepository> _connectionsMock;
    private readonly GmailSyncService _service;

    public GmailSyncServiceTests()
    {
        _gatewayMock = new Mock<IGmailGateway>();
        _mapperMock = new Mock<IGmailItemMapper>();
        _itemsMock = new Mock<IItemRepository>();
        _importantContactsMock = new Mock<IImportantContactRepository>();
        _connectionsMock = new Mock<IConnectionRepository>();

        _service = new GmailSyncService(
            _gatewayMock.Object,
            _mapperMock.Object,
            _itemsMock.Object,
            _importantContactsMock.Object,
            _connectionsMock.Object);

        _importantContactsMock.Setup(m => m.GetIdentifiersAsync(It.IsAny<Guid>(), It.IsAny<ImportantContactType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
    }

    [Fact]
    public async Task FullSync_WhenCursorNull_CreatesAllItems()
    {
        var connection = new Connection { Id = Guid.NewGuid(), UserId = Guid.NewGuid() };
        
        _gatewayMock.Setup(m => m.GetProfileAsync(connection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailProfile("a", 100, 10, 10));

        _gatewayMock.Setup(m => m.ListMessageIdsAsync(connection, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailMessageList(new List<string> { "1", "2", "3" }, null));

        _itemsMock.Setup(m => m.GetExistingExternalIdsAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        _gatewayMock.Setup(m => m.GetMessageAsync(connection, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection c, string id, CancellationToken ct) => 
                new GmailMessage(id, "t", "S", null, new List<string>(), null, new List<string>(), false, null));

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

        _gatewayMock.Setup(m => m.ListMessageIdsAsync(connection, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailMessageList(new List<string> { "1", "2", "3" }, null));

        _itemsMock.Setup(m => m.GetExistingExternalIdsAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "2" });

        _gatewayMock.Setup(m => m.GetMessageAsync(connection, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection c, string id, CancellationToken ct) => 
                new GmailMessage(id, "t", "S", null, new List<string>(), null, new List<string>(), false, null));

        _mapperMock.Setup(m => m.ToItem(It.IsAny<GmailMessage>(), connection.UserId, connection.Id, It.IsAny<ISet<string>>()))
            .Returns((GmailMessage msg, Guid u, Guid c, ISet<string> s) => new Item { ExternalId = msg.Id });

        var result = await _service.SyncConnectionAsync(connection);

        result.Created.Should().Be(2);
        result.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task IncrementalSync_WhenNotExpired_CallsHistoryAndCreatesItems()
    {
        var connection = new Connection { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), CursorValue = "50", CursorType = CursorType.HistoryId };

        _itemsMock.Setup(m => m.GetExistingExternalIdsAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        _gatewayMock.Setup(m => m.ListHistoryAsync(connection, "50", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailHistory(false, new List<string> { "m1", "m2" }, null, "120"));

        _gatewayMock.Setup(m => m.GetMessageAsync(connection, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection c, string id, CancellationToken ct) => 
                new GmailMessage(id, "t", "S", null, new List<string>(), null, new List<string>(), false, null));

        _mapperMock.Setup(m => m.ToItem(It.IsAny<GmailMessage>(), connection.UserId, connection.Id, It.IsAny<ISet<string>>()))
            .Returns((GmailMessage msg, Guid u, Guid c, ISet<string> s) => new Item { ExternalId = msg.Id });

        var result = await _service.SyncConnectionAsync(connection);

        _gatewayMock.Verify(m => m.ListMessageIdsAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

        result.Created.Should().Be(2);
        result.NewCursor.Should().Be("120");
        connection.CursorValue.Should().Be("120");
    }

    [Fact]
    public async Task IncrementalSync_WhenExpired_FallsBackToFullSync()
    {
        var connection = new Connection { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), CursorValue = "50", CursorType = CursorType.HistoryId };

        _itemsMock.Setup(m => m.GetExistingExternalIdsAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        _gatewayMock.Setup(m => m.ListHistoryAsync(connection, "50", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailHistory(true, new List<string>(), null, null));

        _gatewayMock.Setup(m => m.GetProfileAsync(connection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailProfile("a", 200, 10, 10));

        _gatewayMock.Setup(m => m.ListMessageIdsAsync(connection, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailMessageList(new List<string> { "f1" }, null));

        _gatewayMock.Setup(m => m.GetMessageAsync(connection, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection c, string id, CancellationToken ct) => 
                new GmailMessage(id, "t", "S", null, new List<string>(), null, new List<string>(), false, null));

        _mapperMock.Setup(m => m.ToItem(It.IsAny<GmailMessage>(), connection.UserId, connection.Id, It.IsAny<ISet<string>>()))
            .Returns((GmailMessage msg, Guid u, Guid c, ISet<string> s) => new Item { ExternalId = msg.Id });

        var result = await _service.SyncConnectionAsync(connection);

        _gatewayMock.Verify(m => m.ListMessageIdsAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);

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

        _gatewayMock.Setup(m => m.ListMessageIdsAsync(connection, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailMessageList(new List<string>(), null));

        _itemsMock.Setup(m => m.GetExistingExternalIdsAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        var result = await _service.SyncConnectionAsync(connection);

        result.Created.Should().Be(0);
        
        connection.CursorValue.Should().Be("100");
        connection.LastSyncedAt.Should().NotBeNull();
        _connectionsMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
