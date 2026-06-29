using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class ItemWriteBackServiceDeleteTicketTests
{
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IGmailGateway> _gmail = new();
    private readonly Mock<ICalendarGateway> _calendar = new();
    private readonly Mock<IDriveGateway> _drive = new();
    private readonly Mock<IJiraGateway> _jira = new();
    private readonly JiraItemMapper _jiraMapper = new();
    private readonly ItemWriteBackService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _connId = Guid.NewGuid();

    public ItemWriteBackServiceDeleteTicketTests()
    {
        _service = new ItemWriteBackService(
            _items.Object, _connections.Object, new WriteBackGuard(NullLogger<WriteBackGuard>.Instance),
            _gmail.Object, _calendar.Object, _drive.Object,
            _jira.Object, _jiraMapper);
    }

    private Item TicketItem() =>
        new() { Id = Guid.NewGuid(), UserId = _userId, ConnectionId = _connId, Type = ItemType.Ticket, ExternalId = "SCRUM-1" };

    private void Setup(Item item)
    {
        _items.Setup(m => m.GetByIdAndUserAsync(item.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _connections.Setup(m => m.GetByIdAsync(_connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection { Id = _connId, UserId = _userId, ServiceType = ServiceType.Jira, ProviderAccountId = "cloud-1" });
    }

    [Fact]
    public async Task DeleteTicket_CallsJiraDeleteThenRemovesLocal()
    {
        var item = TicketItem();
        Setup(item);

        await _service.DeleteItemAsync(item.Id, _userId);

        _jira.Verify(m => m.DeleteIssueAsync(It.IsAny<Connection>(), "SCRUM-1", It.IsAny<CancellationToken>()), Times.Once);
        _items.Verify(m => m.Remove(item), Times.Once);
        _items.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteTicket_JiraForbidden_PropagatesAndKeepsLocalItem()
    {
        var item = TicketItem();
        Setup(item);
        _jira.Setup(m => m.DeleteIssueAsync(It.IsAny<Connection>(), "SCRUM-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Thiếu quyền xoá issue Jira."));

        var act = () => _service.DeleteItemAsync(item.Id, _userId);

        await act.Should().ThrowAsync<ForbiddenException>();
        // AC: Jira lỗi → KHÔNG xoá lệch local.
        _items.Verify(m => m.Remove(It.IsAny<Item>()), Times.Never);
        _items.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteTicket_ProviderError_Propagates()
    {
        var item = TicketItem();
        Setup(item);
        _jira.Setup(m => m.DeleteIssueAsync(It.IsAny<Connection>(), "SCRUM-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderException("Jira API error 500"));

        var act = () => _service.DeleteItemAsync(item.Id, _userId);

        await act.Should().ThrowAsync<ProviderException>();
        _items.Verify(m => m.Remove(It.IsAny<Item>()), Times.Never);
    }

    [Fact]
    public async Task DeleteTicket_ItemNotFoundOrNotOwner_Throws404()
    {
        var id = Guid.NewGuid();
        _items.Setup(m => m.GetByIdAndUserAsync(id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync((Item?)null);

        var act = () => _service.DeleteItemAsync(id, _userId);

        await act.Should().ThrowAsync<NotFoundException>();
        _jira.Verify(m => m.DeleteIssueAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
