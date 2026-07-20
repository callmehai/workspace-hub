using System.Text.Json;
using FluentAssertions;
using Moq;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Tests.Services;

public class SyncItemNotificationServiceTests
{
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly SyncItemNotificationService _service;

    public SyncItemNotificationServiceTests()
    {
        _service = new SyncItemNotificationService(_items.Object, _notifications.Object);
    }

    [Fact]
    public async Task NotifyNewItemsAsync_Email_UsesI18nKeyAndFromInBody()
    {
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        var item = new Item
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConnectionId = connId,
            Type = ItemType.Email,
            Title = "Weekly report",
            Snippet = "Please review",
            ExternalId = "msg-1",
            MetadataJson = """{"from":"Alice <alice@example.com>"}""",
        };

        _items.Setup(r => r.GetTrackedByConnectionIdAsync(connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item> { [item.ExternalId!] = item });

        string? capturedTitle = null;
        string? capturedBody = null;
        _notifications.Setup(n => n.CreateAndSendAsync(
                userId,
                NotificationType.ItemSynced,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, NotificationType, string, string, string, CancellationToken>(
                (_, _, title, body, _, _) =>
                {
                    capturedTitle = title;
                    capturedBody = body;
                })
            .ReturnsAsync(new WorkspaceHub.Application.DTOs.Notifications.NotificationDto());

        await _service.NotifyNewItemsAsync(connId, userId, new HashSet<string>());

        capturedTitle.Should().Be("notifications.newEmailFrom");
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("from").GetString().Should().Be("Alice");
        doc.RootElement.GetProperty("preview").GetString().Should().Be("Please review");
    }

    [Fact]
    public async Task NotifyNewItemsAsync_Event_UsesEventTitleKey()
    {
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        var item = new Item
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConnectionId = connId,
            Type = ItemType.Event,
            Title = "Standup",
            Snippet = "Daily sync",
            ExternalId = "evt-1",
        };

        _items.Setup(r => r.GetTrackedByConnectionIdAsync(connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item> { [item.ExternalId!] = item });

        string? capturedTitle = null;
        _notifications.Setup(n => n.CreateAndSendAsync(
                userId,
                NotificationType.ItemSynced,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, NotificationType, string, string, string, CancellationToken>(
                (_, _, title, _, _, _) => capturedTitle = title)
            .ReturnsAsync(new WorkspaceHub.Application.DTOs.Notifications.NotificationDto());

        await _service.NotifyNewItemsAsync(connId, userId, new HashSet<string>());

        capturedTitle.Should().Be("notifications.newEvent");
    }

    [Theory]
    [InlineData("TRASH")]
    [InlineData("SPAM")]
    public async Task NotifyNewItemsAsync_EmailInTrashOrSpam_DoesNotNotify(string gmailLabel)
    {
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        var item = new Item
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConnectionId = connId,
            Type = ItemType.Email,
            Title = "Old message",
            Snippet = "Moved mailbox",
            ExternalId = "msg-trash",
            MetadataJson = $$"""{"from":"Bob <bob@example.com>","labels":["{{gmailLabel}}"]}""",
        };

        _items.Setup(r => r.GetTrackedByConnectionIdAsync(connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item> { [item.ExternalId!] = item });

        await _service.NotifyNewItemsAsync(connId, userId, new HashSet<string>());

        _notifications.Verify(n => n.CreateAndSendAsync(
                It.IsAny<Guid>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task NotifyNewItemsAsync_WhenMoreThan10NewItems_CapsImportantAt10()
    {
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        var items = Enumerable.Range(1, 15)
            .Select(i => new Item
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ConnectionId = connId,
                Type = ItemType.Email,
                Title = $"Mail {i}",
                ExternalId = $"msg-{i}",
                IsImportant = true,
            })
            .ToDictionary(i => i.ExternalId!);

        _items.Setup(r => r.GetTrackedByConnectionIdAsync(connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var notifyCount = 0;
        _notifications.Setup(n => n.CreateAndSendAsync(
                userId,
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, NotificationType, string, string, string, CancellationToken>((_, _, _, _, _, _) => notifyCount++)
            .ReturnsAsync(new WorkspaceHub.Application.DTOs.Notifications.NotificationDto());

        await _service.NotifyNewItemsAsync(connId, userId, new HashSet<string>());

        notifyCount.Should().Be(10);
    }

    [Fact]
    public async Task NotifyNewItemsAsync_WhenMoreThan10AndNoImportant_SendsNone()
    {
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        var items = Enumerable.Range(1, 12)
            .Select(i => new Item
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ConnectionId = connId,
                Type = ItemType.Email,
                Title = $"Mail {i}",
                ExternalId = $"msg-{i}",
                IsImportant = false,
            })
            .ToDictionary(i => i.ExternalId!);

        _items.Setup(r => r.GetTrackedByConnectionIdAsync(connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var notifyCount = 0;
        _notifications.Setup(n => n.CreateAndSendAsync(
                userId,
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, NotificationType, string, string, string, CancellationToken>((_, _, _, _, _, _) => notifyCount++)
            .ReturnsAsync(new WorkspaceHub.Application.DTOs.Notifications.NotificationDto());

        await _service.NotifyNewItemsAsync(connId, userId, new HashSet<string>());

        notifyCount.Should().Be(0);
    }
}
