using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.DTOs.Notifications;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

/// <summary>
/// Notification mời chia sẻ folder phải theo ĐÚNG hợp đồng i18n của FE
/// (<c>frontend/src/lib/notificationDisplay.ts</c>):
///   • title = KEY <c>notifications.*</c> — FE dịch + interpolate, KHÔNG phải câu hoàn chỉnh;
///   • body = JSON với field FE biết (<c>from</c>, <c>itemTitle</c>) — KHÔNG đặt tên tuỳ ý.
///
/// Trước đây title ghi thẳng câu tiếng Việt và body dùng field <c>folder</c>: FE rơi vào nhánh
/// legacy, không tìm thấy <c>preview</c> nên fallback in NGUYÊN chuỗi JSON kèm escape unicode ra
/// dropdown thông báo.
/// </summary>
public class FolderServiceShareNotificationTests
{
    private readonly Mock<IFolderRepository> _folderRepo = new();
    private readonly Mock<IItemRepository> _itemRepo = new();
    private readonly Mock<IFriendshipRepository> _friendships = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly FolderService _service;

    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _friendId = Guid.NewGuid();
    private readonly Guid _folderId = Guid.NewGuid();

    private const string OwnerName = "Hoàng Đức Lộc";
    private const string FolderName = "Share with Loc2";

    public FolderServiceShareNotificationTests()
    {
        _service = new FolderService(
            _folderRepo.Object, _itemRepo.Object, _friendships.Object,
            _notifications.Object, NullLogger<FolderService>.Instance);
    }

    private void SetupHappyPath()
    {
        var folder = new Folder
        {
            Id = _folderId,
            OwnerId = _ownerId,
            Name = FolderName,
            Owner = new User { Id = _ownerId, FullName = OwnerName, Email = "owner@example.com" },
        };

        _folderRepo.Setup(r => r.GetByIdWithOwnerAsync(_folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folder);
        _friendships.Setup(f => f.GetBetweenAsync(_ownerId, _friendId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Friendship
            {
                Id = Guid.NewGuid(),
                RequesterId = _ownerId,
                AddresseeId = _friendId,
                Status = FriendshipStatus.Accepted,
            });
        _folderRepo.Setup(r => r.ShareExistsAsync(_folderId, _friendId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _folderRepo.Setup(r => r.GetShareByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new FolderShare
            {
                Id = id,
                FolderId = _folderId,
                SharedWithUserId = _friendId,
                CreatedByUserId = _ownerId,
                Permission = SharePermission.Viewer,
                CreatedAt = DateTime.UtcNow,
                Folder = folder,
                SharedWithUser = new User { Id = _friendId, FullName = "Loc2", Email = "loc2@example.com" },
            });
    }

    [Fact]
    public async Task InviteShare_SendsNotification_WithI18nKeyTitle()
    {
        SetupHappyPath();

        await _service.InviteShareAsync(
            _folderId, _ownerId, new InviteFolderShareRequest(_friendId, "Viewer"));

        _notifications.Verify(n => n.CreateAndSendAsync(
            _friendId,
            NotificationType.ShareInvite,
            "notifications.shareInvite", // key, KHÔNG phải câu tiếng Việt
            It.IsAny<string>(),
            "/",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InviteShare_NotificationBody_UsesFieldNamesFrontendReads()
    {
        SetupHappyPath();
        string? capturedBody = null;
        _notifications
            .Setup(n => n.CreateAndSendAsync(
                It.IsAny<Guid>(), It.IsAny<NotificationType>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, NotificationType __, string ___, string body, string ____, CancellationToken _____) =>
                capturedBody = body)
            .ReturnsAsync((NotificationDto)null!);

        await _service.InviteShareAsync(
            _folderId, _ownerId, new InviteFolderShareRequest(_friendId, "Viewer"));

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        var root = doc.RootElement;

        // FE chỉ interpolate {from} và {itemTitle}; field khác (vd "folder") sẽ bị bỏ qua
        // → thông báo mất tên thư mục.
        root.GetProperty("from").GetString().Should().Be(OwnerName);
        root.GetProperty("itemTitle").GetString().Should().Be(FolderName);
    }

    [Fact]
    public async Task InviteShare_NotificationFails_DoesNotBreakSharing()
    {
        SetupHappyPath();
        _notifications
            .Setup(n => n.CreateAndSendAsync(
                It.IsAny<Guid>(), It.IsAny<NotificationType>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub down"));

        // Notification là best-effort — hub chết không được làm hỏng việc chia sẻ.
        var act = () => _service.InviteShareAsync(
            _folderId, _ownerId, new InviteFolderShareRequest(_friendId, "Viewer"));

        await act.Should().NotThrowAsync();
    }
}
