using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class FolderServiceShareNotificationTests
{
    private readonly Mock<IFolderRepository> _folders = new();
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<IFriendshipRepository> _friendships = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<ILogger<FolderService>> _logger = new();
    private readonly FolderService _service;

    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid FriendId = Guid.NewGuid();
    private static readonly Guid FolderId = Guid.NewGuid();
    private static readonly Guid ShareId = Guid.NewGuid();

    public FolderServiceShareNotificationTests()
    {
        _service = new FolderService(
            _folders.Object, _items.Object, _friendships.Object,
            _notifications.Object, _logger.Object);
    }

    [Fact]
    public async Task InviteShare_SendsShareInviteWithI18nKeyAndPayload()
    {
        var folder = new Folder
        {
            Id = FolderId,
            OwnerId = OwnerId,
            Name = "Dự án A",
            Owner = new User { Id = OwnerId, FullName = "Owner A" },
        };
        var share = new FolderShare
        {
            Id = ShareId,
            FolderId = FolderId,
            SharedWithUserId = FriendId,
            CreatedByUserId = OwnerId,
            Permission = SharePermission.Viewer,
            Folder = folder,
            SharedWithUser = new User { Id = FriendId, FullName = "Friend B" },
        };

        _folders.Setup(r => r.GetByIdWithOwnerAsync(FolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folder);
        _friendships.Setup(r => r.GetBetweenAsync(OwnerId, FriendId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Friendship { Status = FriendshipStatus.Accepted });
        _folders.Setup(r => r.GetShareByFolderAndUserAsync(FolderId, FriendId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FolderShare?)null);
        _folders.Setup(r => r.GetShareByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        await _service.InviteShareAsync(
            FolderId,
            OwnerId,
            new InviteFolderShareRequest(FriendId, "Viewer"));

        _notifications.Verify(n => n.CreateAndSendAsync(
            FriendId,
            NotificationType.ShareInvite,
            "notifications.shareInvite",
            It.Is<string>(body => IsShareInviteBody(body, ShareId, FolderId)),
            "/",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcceptShare_NotifiesOwnerWithShareAccepted()
    {
        var folder = new Folder
        {
            Id = FolderId,
            OwnerId = OwnerId,
            Name = "Dự án A",
            Owner = new User { Id = OwnerId, FullName = "Owner A" },
        };
        var share = new FolderShare
        {
            Id = ShareId,
            FolderId = FolderId,
            SharedWithUserId = FriendId,
            CreatedByUserId = OwnerId,
            Permission = SharePermission.Editor,
            AcceptedAt = null,
            Folder = folder,
            SharedWithUser = new User { Id = FriendId, FullName = "Friend B" },
        };

        _folders.Setup(r => r.GetShareByIdAsync(ShareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        await _service.AcceptShareAsync(ShareId, FriendId);

        _notifications.Verify(n => n.CreateAndSendAsync(
            OwnerId,
            NotificationType.ShareAccepted,
            "notifications.shareAccepted",
            It.Is<string>(body => IsShareAcceptedBody(body, ShareId, FolderId)),
            "/",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeclineShare_NotifiesOwnerWithShareDeclined()
    {
        var folder = new Folder
        {
            Id = FolderId,
            OwnerId = OwnerId,
            Name = "Dự án A",
            Owner = new User { Id = OwnerId, FullName = "Owner A" },
        };
        var share = new FolderShare
        {
            Id = ShareId,
            FolderId = FolderId,
            SharedWithUserId = FriendId,
            CreatedByUserId = OwnerId,
            Permission = SharePermission.Viewer,
            AcceptedAt = null,
            Folder = folder,
            SharedWithUser = new User { Id = FriendId, FullName = "Friend B" },
        };

        _folders.Setup(r => r.GetShareByIdAsync(ShareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        await _service.DeclineShareAsync(ShareId, FriendId);

        share.DeclinedAt.Should().NotBeNull();
        _notifications.Verify(n => n.CreateAndSendAsync(
            OwnerId,
            NotificationType.ShareDeclined,
            "notifications.shareDeclined",
            It.Is<string>(body => IsShareDeclinedBody(body, ShareId, FolderId)),
            "/",
            It.IsAny<CancellationToken>()), Times.Once);
        _folders.Verify(r => r.RemoveShare(It.IsAny<FolderShare>()), Times.Never);
    }

    [Fact]
    public async Task InviteShare_AfterDecline_ReinvitesOnSameRow()
    {
        var folder = new Folder
        {
            Id = FolderId,
            OwnerId = OwnerId,
            Name = "Dự án A",
            Owner = new User { Id = OwnerId, FullName = "Owner A" },
        };
        var declinedShare = new FolderShare
        {
            Id = ShareId,
            FolderId = FolderId,
            SharedWithUserId = FriendId,
            CreatedByUserId = OwnerId,
            Permission = SharePermission.Viewer,
            DeclinedAt = DateTime.UtcNow,
            Folder = folder,
            SharedWithUser = new User { Id = FriendId, FullName = "Friend B" },
        };
        var reinvitedShare = new FolderShare
        {
            Id = ShareId,
            FolderId = FolderId,
            SharedWithUserId = FriendId,
            CreatedByUserId = OwnerId,
            Permission = SharePermission.Editor,
            DeclinedAt = null,
            AcceptedAt = null,
            Folder = folder,
            SharedWithUser = new User { Id = FriendId, FullName = "Friend B" },
        };

        _folders.Setup(r => r.GetByIdWithOwnerAsync(FolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folder);
        _friendships.Setup(r => r.GetBetweenAsync(OwnerId, FriendId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Friendship { Status = FriendshipStatus.Accepted });
        _folders.Setup(r => r.GetShareByFolderAndUserAsync(FolderId, FriendId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(declinedShare);
        _folders.Setup(r => r.GetShareByIdAsync(ShareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reinvitedShare);

        var result = await _service.InviteShareAsync(
            FolderId,
            OwnerId,
            new InviteFolderShareRequest(FriendId, "Editor"));

        result.Status.Should().Be("Pending");
        result.Permission.Should().Be("Editor");
        declinedShare.DeclinedAt.Should().BeNull();
        _folders.Verify(r => r.AddShareAsync(It.IsAny<FolderShare>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifications.Verify(n => n.CreateAndSendAsync(
            FriendId,
            NotificationType.ShareInvite,
            "notifications.shareInvite",
            It.IsAny<string>(),
            "/",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static bool IsShareInviteBody(string body, Guid shareId, Guid folderId)
    {
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("shareId").GetGuid() == shareId
            && doc.RootElement.GetProperty("folderId").GetGuid() == folderId
            && doc.RootElement.GetProperty("folder").GetString() == "Dự án A"
            && doc.RootElement.GetProperty("from").GetString() == "Owner A"
            && doc.RootElement.GetProperty("permission").GetString() == "Viewer";
    }

    private static bool IsShareAcceptedBody(string body, Guid shareId, Guid folderId)
    {
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("shareId").GetGuid() == shareId
            && doc.RootElement.GetProperty("folderId").GetGuid() == folderId
            && doc.RootElement.GetProperty("from").GetString() == "Friend B";
    }

    private static bool IsShareDeclinedBody(string body, Guid shareId, Guid folderId)
    {
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("shareId").GetGuid() == shareId
            && doc.RootElement.GetProperty("folderId").GetGuid() == folderId
            && doc.RootElement.GetProperty("from").GetString() == "Friend B";
    }
}
