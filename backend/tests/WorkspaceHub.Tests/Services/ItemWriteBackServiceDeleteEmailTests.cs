using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
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

/// <summary>
/// Xoá Email gộp thread: phải trash CẢ thread trên Gmail + xoá mọi Item row cùng ThreadId
/// (không phải chỉ trash/remove thư đại diện — nếu vậy thread hiện lại ở list với thư mới-nhì).
/// </summary>
public class ItemWriteBackServiceDeleteEmailTests
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

    public ItemWriteBackServiceDeleteEmailTests()
    {
        _service = new ItemWriteBackService(
            _items.Object, _connections.Object, new WriteBackGuard(Microsoft.Extensions.Logging.Abstractions.NullLogger<WriteBackGuard>.Instance),
            _gmail.Object, _calendar.Object, _drive.Object,
            _jira.Object, _jiraMapper);
    }

    private Item EmailItem(string? threadId) =>
        new() { Id = Guid.NewGuid(), UserId = _userId, ConnectionId = _connId, Type = ItemType.Email, ExternalId = "msg-latest", ThreadId = threadId };

    private void SetupConn(Item item)
    {
        _items.Setup(m => m.GetByIdAndUserAsync(item.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _connections.Setup(m => m.GetByIdAsync(_connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection { Id = _connId, UserId = _userId, ServiceType = ServiceType.Gmail, ProviderAccountId = "me@gmail.com" });
    }

    [Fact]
    public async Task DeleteEmail_WithThread_TrashesWholeThreadAndDeletesAllRows()
    {
        var item = EmailItem("thread-1");
        SetupConn(item);

        await _service.DeleteItemAsync(item.Id, _userId);

        _gmail.Verify(m => m.TrashThreadAsync(It.IsAny<Connection>(), "thread-1", It.IsAny<CancellationToken>()), Times.Once);
        _items.Verify(m => m.DeleteThreadAsync(_userId, "thread-1", It.IsAny<CancellationToken>()), Times.Once);
        // KHÔNG dùng đường xoá 1 message / 1 row.
        _gmail.Verify(m => m.TrashMessageAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _items.Verify(m => m.Remove(It.IsAny<Item>()), Times.Never);
    }

    [Fact]
    public async Task DeleteEmail_GmailError_PropagatesAndKeepsLocalRows()
    {
        var item = EmailItem("thread-1");
        SetupConn(item);
        _gmail.Setup(m => m.TrashThreadAsync(It.IsAny<Connection>(), "thread-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderException("Gmail API error 500"));

        var act = () => _service.DeleteItemAsync(item.Id, _userId);

        await act.Should().ThrowAsync<ProviderException>();
        // Provider lỗi → KHÔNG xoá lệch local.
        _items.Verify(m => m.DeleteThreadAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteEmail_NoThreadId_FallsBackToSingleMessageTrash()
    {
        var item = EmailItem(threadId: null);
        SetupConn(item);

        await _service.DeleteItemAsync(item.Id, _userId);

        _gmail.Verify(m => m.TrashMessageAsync(It.IsAny<Connection>(), "msg-latest", It.IsAny<CancellationToken>()), Times.Once);
        _items.Verify(m => m.Remove(item), Times.Once);
        _gmail.Verify(m => m.TrashThreadAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
