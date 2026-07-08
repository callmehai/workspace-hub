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
}
