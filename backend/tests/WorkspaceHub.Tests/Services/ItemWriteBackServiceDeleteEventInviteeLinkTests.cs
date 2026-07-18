using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class ItemWriteBackServiceDeleteEventInviteeLinkTests
{
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IGmailGateway> _gmail = new();
    private readonly Mock<ICalendarGateway> _calendar = new();
    private readonly Mock<IDriveGateway> _drive = new();
    private readonly Mock<IJiraGateway> _jira = new();
    private readonly Mock<ICalendarInvitationService> _invitations = new();
    private readonly ItemWriteBackService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _connId = Guid.NewGuid();

    public ItemWriteBackServiceDeleteEventInviteeLinkTests()
    {
        _service = new ItemWriteBackService(
            _items.Object, _connections.Object, new WriteBackGuard(NullLogger<WriteBackGuard>.Instance),
            _gmail.Object, _calendar.Object, _drive.Object,
            _jira.Object, new JiraItemMapper(),
            _invitations.Object);
    }

    [Fact]
    public async Task DeleteItem_Event_ClearsInviteeItemLinksBeforeRemove()
    {
        var itemId = Guid.NewGuid();
        var item = new Item
        {
            Id = itemId,
            UserId = _userId,
            ConnectionId = _connId,
            Type = ItemType.Event,
            ExternalId = "gcal-ev-1"
        };

        _items.Setup(m => m.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        _connections.Setup(m => m.GetByIdAsync(_connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection
            {
                Id = _connId,
                UserId = _userId,
                ServiceType = ServiceType.GCal,
                Status = ConnectionStatus.Active
            });
        _calendar.Setup(m => m.DeleteEventAsync(
                It.IsAny<Connection>(), "primary", "gcal-ev-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _service.DeleteItemAsync(itemId, _userId);

        _invitations.Verify(
            m => m.ClearInviteeItemLinksAsync(
                It.Is<IEnumerable<Guid>>(ids => ids.Single() == itemId),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _items.Verify(m => m.Remove(item), Times.Once);
        _items.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
