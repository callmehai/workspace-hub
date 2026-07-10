using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class SendEmailServiceDraftTests
{
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IGmailGateway> _gmail = new();
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<ILogger<SendEmailService>> _logger = new();
    private readonly SendEmailService _service;

    public SendEmailServiceDraftTests()
    {
        _service = new SendEmailService(_connections.Object, _gmail.Object, _items.Object, _logger.Object);
    }

    private void SetupGmail(Guid userId, Guid connId)
    {
        _connections.Setup(m => m.GetByIdTrackedAsync(connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection
            {
                Id = connId,
                UserId = userId,
                ProviderAccountId = "me@gmail.com",
                ServiceType = ServiceType.Gmail,
                Status = ConnectionStatus.Active
            });
    }

    [Fact]
    public async Task SaveDraftAsync_NewDraft_CreatesItemAndCallsGateway()
    {
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        SetupGmail(userId, connId);

        _gmail.Setup(g => g.CreateDraftAsync(
                It.IsAny<Connection>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<GmailAttachmentData>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailDraftResult("draft-123", "msg-123", "thread-123"));

        Item? addedItem = null;
        _items.Setup(i => i.AddAsync(It.IsAny<Item>(), It.IsAny<CancellationToken>()))
            .Callback<Item, CancellationToken>((item, _) => addedItem = item)
            .Returns(Task.CompletedTask);

        var request = new SaveDraftRequest
        {
            ConnectionId = connId,
            To = new List<string> { "bob@gmail.com" },
            Subject = "draft subject",
            BodyHtml = "<p>draft content</p>"
        };

        var response = await _service.SaveDraftAsync(userId, request, null);

        response.ExternalId.Should().Be("msg-123");
        response.Title.Should().Be("draft subject");
        response.Snippet.Should().Be("<p>draft content</p>");
        addedItem.Should().NotBeNull();
        addedItem!.ExternalId.Should().Be("msg-123");
        addedItem.ThreadId.Should().Be("thread-123");

        var meta = JsonSerializer.Deserialize<Dictionary<string, object>>(addedItem.MetadataJson);
        meta.Should().NotBeNull();
        meta!["draftId"].ToString().Should().Be("draft-123");
    }

    [Fact]
    public async Task SaveDraftAsync_ExistingDraft_UpdatesItemAndCallsGateway()
    {
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        SetupGmail(userId, connId);

        var existingItemId = Guid.NewGuid();
        var existingItem = new Item
        {
            Id = existingItemId,
            UserId = userId,
            ConnectionId = connId,
            Type = ItemType.Email,
            ExternalId = "msg-old",
            ThreadId = "thread-old",
            MetadataJson = "{\"draftId\":\"draft-123\",\"labels\":[\"DRAFT\"]}"
        };

        _items.Setup(i => i.GetByIdAsync(existingItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingItem);

        _gmail.Setup(g => g.UpdateDraftAsync(
                It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<GmailAttachmentData>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GmailDraftResult("draft-123", "msg-new", "thread-old"));

        var request = new SaveDraftRequest
        {
            ConnectionId = connId,
            To = new List<string> { "bob@gmail.com" },
            Subject = "draft subject updated",
            BodyHtml = "<p>draft content updated</p>"
        };

        var response = await _service.SaveDraftAsync(userId, request, existingItemId);

        response.ExternalId.Should().Be("msg-new");
        response.Title.Should().Be("draft subject updated");
        response.Snippet.Should().Be("<p>draft content updated</p>");
        existingItem.ExternalId.Should().Be("msg-new");

        var meta = JsonSerializer.Deserialize<Dictionary<string, object>>(existingItem.MetadataJson);
        meta.Should().NotBeNull();
        meta!["draftId"].ToString().Should().Be("draft-123");
    }

    [Fact]
    public async Task SendDraftAsync_ValidDraft_CallsGatewayAndRemovesDraftLabel()
    {
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        SetupGmail(userId, connId);

        var itemId = Guid.NewGuid();
        var existingItem = new Item
        {
            Id = itemId,
            UserId = userId,
            ConnectionId = connId,
            Type = ItemType.Email,
            ExternalId = "msg-123",
            ThreadId = "thread-123",
            MetadataJson = "{\"draftId\":\"draft-123\",\"labels\":[\"DRAFT\"]}"
        };

        _items.Setup(i => i.GetByIdAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingItem);

        _gmail.Setup(g => g.SendDraftAsync(It.IsAny<Connection>(), "draft-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync("msg-sent-id");

        var result = await _service.SendDraftAsync(userId, itemId);

        result.MessageId.Should().Be("msg-sent-id");
        existingItem.ExternalId.Should().Be("msg-sent-id");

        var meta = JsonSerializer.Deserialize<Dictionary<string, object>>(existingItem.MetadataJson);
        meta.Should().NotBeNull();
        meta!.ContainsKey("draftId").Should().BeFalse();
        
        var labelsElement = (JsonElement)meta["labels"];
        var labels = new List<string>();
        foreach (var label in labelsElement.EnumerateArray())
        {
            labels.Add(label.GetString() ?? "");
        }
        labels.Should().NotContain("DRAFT");
        labels.Should().Contain("SENT");
    }

    [Fact]
    public async Task DiscardDraftAsync_ValidDraft_CallsGatewayAndRemovesItem()
    {
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        SetupGmail(userId, connId);

        var itemId = Guid.NewGuid();
        var existingItem = new Item
        {
            Id = itemId,
            UserId = userId,
            ConnectionId = connId,
            Type = ItemType.Email,
            ExternalId = "msg-123",
            ThreadId = "thread-123",
            MetadataJson = "{\"draftId\":\"draft-123\",\"labels\":[\"DRAFT\"]}"
        };

        _items.Setup(i => i.GetByIdAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingItem);

        bool deleteCalled = false;
        _items.Setup(i => i.Remove(existingItem))
            .Callback(() => deleteCalled = true);

        await _service.DiscardDraftAsync(userId, itemId);

        // DiscardDraftAsync xoá VĨNH VIỄN qua drafts.delete (draftId lấy từ metadata), KHÔNG đẩy vào thùng rác.
        _gmail.Verify(g => g.DeleteDraftAsync(It.IsAny<Connection>(), "draft-123", It.IsAny<CancellationToken>()), Times.Once);
        _gmail.Verify(g => g.TrashMessageAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        deleteCalled.Should().BeTrue();
    }
}
