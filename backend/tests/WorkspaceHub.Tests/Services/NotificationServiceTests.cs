using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Notifications;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class NotificationServiceTests
{
    private readonly Mock<INotificationRepository> _repo = new();
    private readonly Mock<INotificationPublisher> _publisher = new();
    private readonly NotificationService _service;
    private readonly Guid _userId = Guid.NewGuid();

    public NotificationServiceTests()
    {
        _service = new NotificationService(_repo.Object, _publisher.Object);
    }

    [Fact]
    public async Task CreateAndSendAsync_SavesAndPublishes()
    {
        Notification? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback((Notification n, CancellationToken _) => added = n)
            .Returns(Task.CompletedTask);
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await _service.CreateAndSendAsync(
            _userId,
            NotificationType.ItemSynced,
            "Test",
            "Body",
            "/inbox");

        result.Title.Should().Be("Test");
        added.Should().NotBeNull();
        added!.UserId.Should().Be(_userId);
        added.IsRead.Should().BeFalse();
        _repo.Verify(r => r.AddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<Notification>>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _publisher.Verify(
            p => p.PublishToUserAsync(_userId, It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAndSendToManyAsync_BulkInsertsAndPublishesEachUser()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        List<Notification>? added = null;

        _repo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<Notification>>(), It.IsAny<CancellationToken>()))
            .Callback((IEnumerable<Notification> items, CancellationToken _) => added = items.ToList())
            .Returns(Task.CompletedTask);
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var results = await _service.CreateAndSendToManyAsync(
            [userA, userB, userA], // duplicate bị loại
            NotificationType.ShareInvite,
            "Shared",
            "You were invited",
            "/folders/1");

        results.Should().HaveCount(2);
        added.Should().NotBeNull().And.HaveCount(2);
        added!.Select(n => n.UserId).Should().BeEquivalentTo([userA, userB]);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _publisher.Verify(
            p => p.PublishToUserAsync(userA, It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _publisher.Verify(
            p => p.PublishToUserAsync(userB, It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAndSendToManyAsync_EmptyList_NoOp()
    {
        var results = await _service.CreateAndSendToManyAsync(
            [], NotificationType.ShareInvite, "T", "B", "/");

        results.Should().BeEmpty();
        _repo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<Notification>>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisher.Verify(
            p => p.PublishToUserAsync(It.IsAny<Guid>(), It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MarkAsReadAsync_ThrowsWhenNotOwned()
    {
        _repo.Setup(r => r.GetByIdForUserAsync(_userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Notification?)null);

        var act = () => _service.MarkAsReadAsync(_userId, Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task MarkAsReadAsync_UpdatesWhenUnread()
    {
        var id = Guid.NewGuid();
        var notification = new Notification
        {
            Id = id,
            UserId = _userId,
            Type = NotificationType.ItemSynced,
            Title = "T",
            Body = "B",
            LinkUrl = "/",
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _repo.Setup(r => r.GetByIdForUserAsync(_userId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await _service.MarkAsReadAsync(_userId, id);

        notification.IsRead.Should().BeTrue();
        _repo.Verify(r => r.Update(notification), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
