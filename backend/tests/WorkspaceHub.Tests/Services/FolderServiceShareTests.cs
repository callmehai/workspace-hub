using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Tests.Services;

/// <summary>
/// Unit test cho nhóm chia sẻ folder của <see cref="FolderService"/>:
/// invite / list / đổi quyền / thu hồi / shared-with-me / accept / decline / leave.
///
/// Hai khuôn mẫu lỗi hay lặp (xem docs/FOLDER-SHARING-HANDOVER.md) được canh ở đây:
///   • thiếu share-check → người ngoài thao tác được trên share của folder người khác;
///   • share thuộc folder KHÁC vẫn bị sửa/xoá khi chỉ tra theo shareId.
/// Riêng phần nội dung notification nằm ở <c>FolderServiceShareNotificationTests</c>.
/// </summary>
public class FolderServiceShareTests
{
    private readonly Mock<IFolderRepository> _folderRepo = new();
    private readonly Mock<IItemRepository> _itemRepo = new();
    private readonly Mock<IFriendshipRepository> _friendships = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly FolderService _service;

    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _friendId = Guid.NewGuid();
    private readonly Guid _strangerId = Guid.NewGuid();
    private readonly Guid _folderId = Guid.NewGuid();

    public FolderServiceShareTests()
    {
        _service = new FolderService(
            _folderRepo.Object, _itemRepo.Object, _friendships.Object,
            _notifications.Object, NullLogger<FolderService>.Instance);
    }

    private Folder MakeFolder(Guid ownerId, string name = "Dự án") => new()
    {
        Id = _folderId,
        OwnerId = ownerId,
        Name = name,
        Color = "#fff",
        Icon = "folder",
        Owner = new User { Id = ownerId, FullName = "Chủ Sở Hữu", Email = "owner@example.com" }
    };

    private FolderShare MakeShare(
        Guid shareId,
        Guid? folderId = null,
        Guid? sharedWithUserId = null,
        SharePermission permission = SharePermission.Viewer,
        DateTime? acceptedAt = null) => new()
        {
            Id = shareId,
            FolderId = folderId ?? _folderId,
            SharedWithUserId = sharedWithUserId ?? _friendId,
            CreatedByUserId = _ownerId,
            Permission = permission,
            CreatedAt = DateTime.UtcNow,
            AcceptedAt = acceptedAt,
            Folder = MakeFolder(_ownerId),
            SharedWithUser = new User
            {
                Id = sharedWithUserId ?? _friendId,
                FullName = "Bạn Bè",
                Email = "friend@example.com",
                AvatarUrl = "https://cdn/avatar.png"
            }
        };

    /// <summary>Dựng đủ mock cho nhánh invite thành công.</summary>
    private void SetupInviteHappyPath()
    {
        var folder = MakeFolder(_ownerId);
        _folderRepo.Setup(m => m.GetByIdWithOwnerAsync(_folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folder);
        _friendships.Setup(m => m.GetBetweenAsync(_ownerId, _friendId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Friendship
            {
                Id = Guid.NewGuid(),
                RequesterId = _ownerId,
                AddresseeId = _friendId,
                Status = FriendshipStatus.Accepted
            });
        _folderRepo.Setup(m => m.ShareExistsAsync(_folderId, _friendId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _folderRepo.Setup(m => m.GetShareByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => MakeShare(id));
    }

    // ── InviteShareAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task InviteShareAsync_OwnerInvitesFriend_CreatesPendingShare()
    {
        SetupInviteHappyPath();
        FolderShare? added = null;
        _folderRepo.Setup(m => m.AddShareAsync(It.IsAny<FolderShare>(), It.IsAny<CancellationToken>()))
            .Callback((FolderShare s, CancellationToken _) => added = s)
            .Returns(Task.CompletedTask);

        var result = await _service.InviteShareAsync(
            _folderId, _ownerId, new InviteFolderShareRequest(_friendId, "Editor"));

        added.Should().NotBeNull();
        added!.FolderId.Should().Be(_folderId);
        added.SharedWithUserId.Should().Be(_friendId);
        added.CreatedByUserId.Should().Be(_ownerId);
        added.Permission.Should().Be(SharePermission.Editor);
        added.AcceptedAt.Should().BeNull();   // invite luôn ở trạng thái chờ chấp nhận
        result.Status.Should().Be("Pending");
        result.SharedWithUserId.Should().Be(_friendId);
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("viewer", SharePermission.Viewer)]
    [InlineData("EDITOR", SharePermission.Editor)]
    public async Task InviteShareAsync_PermissionCaseInsensitive_Parses(string input, SharePermission expected)
    {
        SetupInviteHappyPath();
        FolderShare? added = null;
        _folderRepo.Setup(m => m.AddShareAsync(It.IsAny<FolderShare>(), It.IsAny<CancellationToken>()))
            .Callback((FolderShare s, CancellationToken _) => added = s)
            .Returns(Task.CompletedTask);

        await _service.InviteShareAsync(
            _folderId, _ownerId, new InviteFolderShareRequest(_friendId, input));

        added!.Permission.Should().Be(expected);
    }

    [Fact]
    public async Task InviteShareAsync_FolderNotFound_Throws404()
    {
        _folderRepo.Setup(m => m.GetByIdWithOwnerAsync(_folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Folder?)null);

        var act = () => _service.InviteShareAsync(
            _folderId, _ownerId, new InviteFolderShareRequest(_friendId, "Viewer"));

        await act.Should().ThrowAsync<NotFoundException>();
        _folderRepo.Verify(m => m.AddShareAsync(It.IsAny<FolderShare>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InviteShareAsync_NotOwner_Throws403()
    {
        // Người được share (kể cả Editor) KHÔNG được share tiếp cho người khác.
        _folderRepo.Setup(m => m.GetByIdWithOwnerAsync(_folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFolder(_ownerId));

        var act = () => _service.InviteShareAsync(
            _folderId, _strangerId, new InviteFolderShareRequest(_friendId, "Viewer"));

        await act.Should().ThrowAsync<ForbiddenException>();
        _folderRepo.Verify(m => m.AddShareAsync(It.IsAny<FolderShare>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InviteShareAsync_ShareWithSelf_Throws422()
    {
        _folderRepo.Setup(m => m.GetByIdWithOwnerAsync(_folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFolder(_ownerId));

        var act = () => _service.InviteShareAsync(
            _folderId, _ownerId, new InviteFolderShareRequest(_ownerId, "Viewer"));

        await act.Should().ThrowAsync<BusinessRuleException>();
        _friendships.Verify(m => m.GetBetweenAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InviteShareAsync_NoFriendship_Throws422()
    {
        _folderRepo.Setup(m => m.GetByIdWithOwnerAsync(_folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFolder(_ownerId));
        _friendships.Setup(m => m.GetBetweenAsync(_ownerId, _friendId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Friendship?)null);

        var act = () => _service.InviteShareAsync(
            _folderId, _ownerId, new InviteFolderShareRequest(_friendId, "Viewer"));

        await act.Should().ThrowAsync<BusinessRuleException>();
        _folderRepo.Verify(m => m.AddShareAsync(It.IsAny<FolderShare>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InviteShareAsync_FriendshipStillPending_Throws422()
    {
        // Chỉ mới gửi lời mời kết bạn, chưa accept → chưa được chia sẻ.
        _folderRepo.Setup(m => m.GetByIdWithOwnerAsync(_folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFolder(_ownerId));
        _friendships.Setup(m => m.GetBetweenAsync(_ownerId, _friendId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Friendship
            {
                Id = Guid.NewGuid(),
                RequesterId = _ownerId,
                AddresseeId = _friendId,
                Status = FriendshipStatus.Pending
            });

        var act = () => _service.InviteShareAsync(
            _folderId, _ownerId, new InviteFolderShareRequest(_friendId, "Viewer"));

        await act.Should().ThrowAsync<BusinessRuleException>();
        _folderRepo.Verify(m => m.AddShareAsync(It.IsAny<FolderShare>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InviteShareAsync_AlreadyShared_Throws409()
    {
        SetupInviteHappyPath();
        // Kể cả share đang pending cũng tính là đã chia sẻ.
        _folderRepo.Setup(m => m.ShareExistsAsync(_folderId, _friendId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var act = () => _service.InviteShareAsync(
            _folderId, _ownerId, new InviteFolderShareRequest(_friendId, "Viewer"));

        await act.Should().ThrowAsync<ConflictException>();
        _folderRepo.Verify(m => m.AddShareAsync(It.IsAny<FolderShare>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InviteShareAsync_InvalidPermission_Throws422()
    {
        SetupInviteHappyPath();

        var act = () => _service.InviteShareAsync(
            _folderId, _ownerId, new InviteFolderShareRequest(_friendId, "SuperAdmin"));

        await act.Should().ThrowAsync<BusinessRuleException>();
        _folderRepo.Verify(m => m.AddShareAsync(It.IsAny<FolderShare>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── GetSharesForFolderAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetSharesForFolderAsync_Owner_MapsShares()
    {
        var acceptedId = Guid.NewGuid();
        var pendingId = Guid.NewGuid();
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _folderRepo.Setup(m => m.GetSharesByFolderAsync(_folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FolderShare>
            {
                MakeShare(acceptedId, permission: SharePermission.Editor, acceptedAt: DateTime.UtcNow),
                MakeShare(pendingId)
            });

        var result = await _service.GetSharesForFolderAsync(_folderId, _ownerId);

        result.Should().HaveCount(2);
        var accepted = result.Single(s => s.ShareId == acceptedId);
        accepted.Status.Should().Be("Accepted");
        accepted.Permission.Should().Be("Editor");
        accepted.SharedWithUserName.Should().Be("Bạn Bè");
        accepted.SharedWithUserAvatar.Should().Be("https://cdn/avatar.png");
        result.Single(s => s.ShareId == pendingId).Status.Should().Be("Pending");
    }

    [Fact]
    public async Task GetSharesForFolderAsync_NotOwner_Throws403()
    {
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _strangerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => _service.GetSharesForFolderAsync(_folderId, _strangerId);

        await act.Should().ThrowAsync<ForbiddenException>();
        _folderRepo.Verify(m => m.GetSharesByFolderAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── UpdateShareRoleAsync ────────────────────────────────────────────────

    [Fact]
    public async Task UpdateShareRoleAsync_Owner_ChangesPermission()
    {
        var shareId = Guid.NewGuid();
        var share = MakeShare(shareId);
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _folderRepo.Setup(m => m.GetShareByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var result = await _service.UpdateShareRoleAsync(
            _folderId, shareId, _ownerId, new UpdateFolderShareRequest("Editor"));

        share.Permission.Should().Be(SharePermission.Editor);
        result.Permission.Should().Be("Editor");
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateShareRoleAsync_NotOwner_Throws403()
    {
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _strangerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => _service.UpdateShareRoleAsync(
            _folderId, Guid.NewGuid(), _strangerId, new UpdateFolderShareRequest("Editor"));

        await act.Should().ThrowAsync<ForbiddenException>();
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateShareRoleAsync_ShareNotFound_Throws404()
    {
        var shareId = Guid.NewGuid();
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _folderRepo.Setup(m => m.GetShareByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FolderShare?)null);

        var act = () => _service.UpdateShareRoleAsync(
            _folderId, shareId, _ownerId, new UpdateFolderShareRequest("Editor"));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateShareRoleAsync_ShareBelongsToOtherFolder_Throws404()
    {
        // shareId hợp lệ nhưng thuộc folder khác → không được sửa "nhờ" qua folder mình sở hữu.
        var shareId = Guid.NewGuid();
        var share = MakeShare(shareId, folderId: Guid.NewGuid());
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _folderRepo.Setup(m => m.GetShareByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var act = () => _service.UpdateShareRoleAsync(
            _folderId, shareId, _ownerId, new UpdateFolderShareRequest("Editor"));

        await act.Should().ThrowAsync<NotFoundException>();
        share.Permission.Should().Be(SharePermission.Viewer);
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateShareRoleAsync_InvalidPermission_Throws422()
    {
        var shareId = Guid.NewGuid();
        var share = MakeShare(shareId);
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _folderRepo.Setup(m => m.GetShareByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var act = () => _service.UpdateShareRoleAsync(
            _folderId, shareId, _ownerId, new UpdateFolderShareRequest("Owner"));

        await act.Should().ThrowAsync<BusinessRuleException>();
        share.Permission.Should().Be(SharePermission.Viewer);
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── RevokeShareAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeShareAsync_Owner_RemovesShare()
    {
        var shareId = Guid.NewGuid();
        var share = MakeShare(shareId);
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _folderRepo.Setup(m => m.GetShareByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        await _service.RevokeShareAsync(_folderId, shareId, _ownerId);

        _folderRepo.Verify(m => m.RemoveShare(share), Times.Once);
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeShareAsync_NotOwner_Throws403()
    {
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _strangerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => _service.RevokeShareAsync(_folderId, Guid.NewGuid(), _strangerId);

        await act.Should().ThrowAsync<ForbiddenException>();
        _folderRepo.Verify(m => m.RemoveShare(It.IsAny<FolderShare>()), Times.Never);
    }

    [Fact]
    public async Task RevokeShareAsync_ShareNotFound_Throws404()
    {
        var shareId = Guid.NewGuid();
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _folderRepo.Setup(m => m.GetShareByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FolderShare?)null);

        var act = () => _service.RevokeShareAsync(_folderId, shareId, _ownerId);

        await act.Should().ThrowAsync<NotFoundException>();
        _folderRepo.Verify(m => m.RemoveShare(It.IsAny<FolderShare>()), Times.Never);
    }

    [Fact]
    public async Task RevokeShareAsync_ShareBelongsToOtherFolder_Throws404()
    {
        var shareId = Guid.NewGuid();
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _folderRepo.Setup(m => m.GetShareByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeShare(shareId, folderId: Guid.NewGuid()));

        var act = () => _service.RevokeShareAsync(_folderId, shareId, _ownerId);

        await act.Should().ThrowAsync<NotFoundException>();
        _folderRepo.Verify(m => m.RemoveShare(It.IsAny<FolderShare>()), Times.Never);
    }

    // ── GetFoldersSharedWithMeAsync ─────────────────────────────────────────

    [Fact]
    public async Task GetFoldersSharedWithMeAsync_MapsOwnerAndStatus()
    {
        var acceptedId = Guid.NewGuid();
        var pendingId = Guid.NewGuid();
        _folderRepo.Setup(m => m.GetSharesForUserAsync(_friendId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FolderShare>
            {
                MakeShare(acceptedId, acceptedAt: DateTime.UtcNow, permission: SharePermission.Editor),
                MakeShare(pendingId)
            });

        var result = await _service.GetFoldersSharedWithMeAsync(_friendId);

        result.Should().HaveCount(2);
        var accepted = result.Single(s => s.ShareId == acceptedId);
        accepted.FolderId.Should().Be(_folderId);
        accepted.FolderName.Should().Be("Dự án");
        accepted.OwnerUserId.Should().Be(_ownerId);
        accepted.OwnerName.Should().Be("Chủ Sở Hữu");
        accepted.Permission.Should().Be("Editor");
        accepted.Status.Should().Be("Accepted");
        result.Single(s => s.ShareId == pendingId).Status.Should().Be("Pending");
    }

    [Fact]
    public async Task GetFoldersSharedWithMeAsync_OwnerNavigationMissing_FallsBackToUnknown()
    {
        var shareId = Guid.NewGuid();
        var share = MakeShare(shareId);
        share.Folder.Owner = null!;   // repo không include Owner (hoặc user đã bị xoá)
        _folderRepo.Setup(m => m.GetSharesForUserAsync(_friendId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FolderShare> { share });

        var result = await _service.GetFoldersSharedWithMeAsync(_friendId);

        result.Should().ContainSingle().Which.OwnerName.Should().Be("Unknown");
    }

    [Fact]
    public async Task GetFoldersSharedWithMeAsync_NoShares_ReturnsEmpty()
    {
        _folderRepo.Setup(m => m.GetSharesForUserAsync(_friendId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FolderShare>());

        var result = await _service.GetFoldersSharedWithMeAsync(_friendId);

        result.Should().BeEmpty();
    }

    // ── AcceptShareAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task AcceptShareAsync_Invitee_SetsAcceptedAt()
    {
        var shareId = Guid.NewGuid();
        var share = MakeShare(shareId);
        _folderRepo.Setup(m => m.GetShareByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var result = await _service.AcceptShareAsync(shareId, _friendId);

        share.AcceptedAt.Should().NotBeNull();
        share.AcceptedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.Status.Should().Be("Accepted");
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcceptShareAsync_ShareNotFound_Throws404()
    {
        var shareId = Guid.NewGuid();
        _folderRepo.Setup(m => m.GetShareByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FolderShare?)null);

        var act = () => _service.AcceptShareAsync(shareId, _friendId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AcceptShareAsync_NotInvitedUser_Throws403()
    {
        var shareId = Guid.NewGuid();
        var share = MakeShare(shareId);
        _folderRepo.Setup(m => m.GetShareByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var act = () => _service.AcceptShareAsync(shareId, _strangerId);

        await act.Should().ThrowAsync<ForbiddenException>();
        share.AcceptedAt.Should().BeNull();
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AcceptShareAsync_AlreadyAccepted_Throws409()
    {
        var shareId = Guid.NewGuid();
        var acceptedAt = DateTime.UtcNow.AddDays(-1);
        var share = MakeShare(shareId, acceptedAt: acceptedAt);
        _folderRepo.Setup(m => m.GetShareByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var act = () => _service.AcceptShareAsync(shareId, _friendId);

        await act.Should().ThrowAsync<ConflictException>();
        share.AcceptedAt.Should().Be(acceptedAt);   // không ghi đè mốc accept cũ
    }

    // ── DeclineShareAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task DeclineShareAsync_Invitee_RemovesShareRow()
    {
        var shareId = Guid.NewGuid();
        var share = MakeShare(shareId);
        _folderRepo.Setup(m => m.GetShareByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        await _service.DeclineShareAsync(shareId, _friendId);

        // Decline = xoá hẳn row (không lưu trạng thái "Declined").
        _folderRepo.Verify(m => m.RemoveShare(share), Times.Once);
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeclineShareAsync_ShareNotFound_Throws404()
    {
        var shareId = Guid.NewGuid();
        _folderRepo.Setup(m => m.GetShareByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FolderShare?)null);

        var act = () => _service.DeclineShareAsync(shareId, _friendId);

        await act.Should().ThrowAsync<NotFoundException>();
        _folderRepo.Verify(m => m.RemoveShare(It.IsAny<FolderShare>()), Times.Never);
    }

    [Fact]
    public async Task DeclineShareAsync_NotInvitedUser_Throws403()
    {
        var shareId = Guid.NewGuid();
        _folderRepo.Setup(m => m.GetShareByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeShare(shareId));

        var act = () => _service.DeclineShareAsync(shareId, _strangerId);

        await act.Should().ThrowAsync<ForbiddenException>();
        _folderRepo.Verify(m => m.RemoveShare(It.IsAny<FolderShare>()), Times.Never);
    }

    // ── LeaveFolderAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task LeaveFolderAsync_HasShare_RemovesOwnShareOnly()
    {
        var share = MakeShare(Guid.NewGuid(), acceptedAt: DateTime.UtcNow);
        _folderRepo.Setup(m => m.GetShareByFolderAndUserAsync(_folderId, _friendId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        await _service.LeaveFolderAsync(_folderId, _friendId);

        _folderRepo.Verify(m => m.RemoveShare(share), Times.Once);
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        // Rời folder KHÔNG được xoá folder của owner.
        _folderRepo.Verify(m => m.Remove(It.IsAny<Folder>()), Times.Never);
    }

    [Fact]
    public async Task LeaveFolderAsync_NoShareForUser_Throws404()
    {
        // User chưa từng được share (hoặc đã bị revoke) → không có gì để rời.
        _folderRepo.Setup(m => m.GetShareByFolderAndUserAsync(_folderId, _strangerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FolderShare?)null);

        var act = () => _service.LeaveFolderAsync(_folderId, _strangerId);

        await act.Should().ThrowAsync<NotFoundException>();
        _folderRepo.Verify(m => m.RemoveShare(It.IsAny<FolderShare>()), Times.Never);
    }
}
