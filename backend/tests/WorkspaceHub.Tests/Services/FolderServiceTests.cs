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
/// Unit test cho phần CRUD folder + gắn/gỡ item của <see cref="FolderService"/>.
/// Phần chia sẻ (invite/accept/decline/revoke/leave) nằm ở <c>FolderServiceShareTests</c>.
///
/// Nguyên tắc chung được kiểm chứng ở đây: mọi thao tác ghi lên folder đều là ĐỘC QUYỀN OWNER,
/// và item phải thuộc chính user gọi API (không mượn item người khác nhét vào folder mình).
/// </summary>
public class FolderServiceTests
{
    private readonly Mock<IFolderRepository> _folderRepo = new();
    private readonly Mock<IItemRepository> _itemRepo = new();
    private readonly Mock<IFriendshipRepository> _friendships = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly FolderService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();
    private readonly Guid _folderId = Guid.NewGuid();

    public FolderServiceTests()
    {
        _service = new FolderService(
            _folderRepo.Object, _itemRepo.Object, _friendships.Object,
            _notifications.Object, NullLogger<FolderService>.Instance);
    }

    // Helper tạo folder tối thiểu (kèm Owner navigation để map OwnerName).
    private static Folder MakeFolder(
        Guid id, Guid ownerId, string name = "Work", string ownerName = "Owner Name") => new()
        {
            Id = id,
            OwnerId = ownerId,
            Name = name,
            Color = "#FF0000",
            Icon = "folder",
            SortOrder = 1,
            IsArchived = false,
            Owner = new User { Id = ownerId, FullName = ownerName, Email = "owner@example.com" }
        };

    private static Item MakeItem(Guid id, Guid userId) =>
        new() { Id = id, UserId = userId, Title = "item", Snippet = "snippet" };

    // ── GetFoldersAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetFoldersAsync_OwnedOnly_MapsComputedFields()
    {
        var folder = MakeFolder(_folderId, _userId, "Dự án A", "Trần Việt");
        folder.ItemFolders = new List<ItemFolder>
        {
            new() { ItemId = Guid.NewGuid(), FolderId = _folderId },
            new() { ItemId = Guid.NewGuid(), FolderId = _folderId }
        };
        _folderRepo.Setup(m => m.GetUserFoldersAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Folder> { folder });

        var result = await _service.GetFoldersAsync(_userId, includeShared: false);

        result.Should().ContainSingle();
        var dto = result[0];
        dto.Id.Should().Be(_folderId);
        dto.Name.Should().Be("Dự án A");
        dto.IsOwner.Should().BeTrue();
        dto.Permission.Should().Be("Owner");
        dto.ItemCount.Should().Be(2);
        dto.OwnerName.Should().Be("Trần Việt");
        // includeShared=false → không được đụng tới query folder chia sẻ.
        _folderRepo.Verify(m => m.GetSharedFoldersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetFoldersAsync_IncludeShared_AppendsSharedWithSharePermission()
    {
        var owned = MakeFolder(_folderId, _userId);
        var sharedFolderId = Guid.NewGuid();
        var shared = MakeFolder(sharedFolderId, _otherUserId, "Của bạn", "Bạn Bè");
        shared.FolderShares = new List<FolderShare>
        {
            new()
            {
                Id = Guid.NewGuid(),
                FolderId = sharedFolderId,
                SharedWithUserId = _userId,
                Permission = SharePermission.Editor
            }
        };

        _folderRepo.Setup(m => m.GetUserFoldersAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Folder> { owned });
        _folderRepo.Setup(m => m.GetSharedFoldersAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Folder> { shared });

        var result = await _service.GetFoldersAsync(_userId, includeShared: true);

        result.Should().HaveCount(2);
        var sharedDto = result.Single(f => f.Id == sharedFolderId);
        sharedDto.IsOwner.Should().BeFalse();
        sharedDto.Permission.Should().Be("Editor");
        sharedDto.OwnerName.Should().Be("Bạn Bè");
    }

    [Fact]
    public async Task GetFoldersAsync_SharedWithoutShareRow_FallsBackToViewer()
    {
        var sharedFolderId = Guid.NewGuid();
        var shared = MakeFolder(sharedFolderId, _otherUserId);
        shared.FolderShares = new List<FolderShare>();   // không tìm thấy share của user

        _folderRepo.Setup(m => m.GetUserFoldersAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Folder>());
        _folderRepo.Setup(m => m.GetSharedFoldersAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Folder> { shared });

        var result = await _service.GetFoldersAsync(_userId, includeShared: true);

        // Mặc định an toàn: chỉ được xem.
        result.Should().ContainSingle().Which.Permission.Should().Be("Viewer");
    }

    [Fact]
    public async Task GetFoldersAsync_FolderBothOwnedAndShared_NotDuplicated()
    {
        var folder = MakeFolder(_folderId, _userId);
        _folderRepo.Setup(m => m.GetUserFoldersAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Folder> { folder });
        _folderRepo.Setup(m => m.GetSharedFoldersAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Folder> { folder });   // edge case vừa own vừa được share

        var result = await _service.GetFoldersAsync(_userId, includeShared: true);

        result.Should().ContainSingle();
        result[0].IsOwner.Should().BeTrue();
    }

    // ── CreateAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_Persists_WithSortOrderMaxPlusOne()
    {
        _folderRepo.Setup(m => m.GetMaxSortOrderAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
        Folder? added = null;
        _folderRepo.Setup(m => m.AddAsync(It.IsAny<Folder>(), It.IsAny<CancellationToken>()))
            .Callback((Folder f, CancellationToken _) => added = f)
            .Returns(Task.CompletedTask);
        _folderRepo.Setup(m => m.GetByIdWithOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => MakeFolder(id, _userId, "Mới"));

        var result = await _service.CreateAsync(_userId, new CreateFolderRequest("Mới", "#00FF00", "star"));

        added.Should().NotBeNull();
        added!.OwnerId.Should().Be(_userId);
        added.SortOrder.Should().Be(8);
        added.IsArchived.Should().BeFalse();
        added.Id.Should().NotBeEmpty();
        result.IsOwner.Should().BeTrue();
        result.Permission.Should().Be("Owner");
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ReloadReturnsNull_ThrowsInvalidOperation()
    {
        _folderRepo.Setup(m => m.GetMaxSortOrderAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _folderRepo.Setup(m => m.GetByIdWithOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Folder?)null);

        var act = () => _service.CreateAsync(_userId, new CreateFolderRequest("Mới", "#000", "x"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── UpdateAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_Owner_AppliesChanges()
    {
        var folder = MakeFolder(_folderId, _userId);
        _folderRepo.Setup(m => m.GetByIdWithOwnerAsync(_folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folder);

        var result = await _service.UpdateAsync(
            _userId, _folderId, new UpdateFolderRequest("Tên mới", "#123456", "rocket", 42));

        folder.Name.Should().Be("Tên mới");
        folder.Color.Should().Be("#123456");
        folder.Icon.Should().Be("rocket");
        folder.SortOrder.Should().Be(42);
        result.Name.Should().Be("Tên mới");
        result.SortOrder.Should().Be(42);
        _folderRepo.Verify(m => m.Update(folder), Times.Once);
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_FolderNotFound_Throws404()
    {
        _folderRepo.Setup(m => m.GetByIdWithOwnerAsync(_folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Folder?)null);

        var act = () => _service.UpdateAsync(
            _userId, _folderId, new UpdateFolderRequest("x", "#000", "i", 1));

        await act.Should().ThrowAsync<NotFoundException>();
        _folderRepo.Verify(m => m.Update(It.IsAny<Folder>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_NotOwner_Throws403()
    {
        // Folder của người khác — kể cả khi user được share cũng KHÔNG được đổi metadata.
        _folderRepo.Setup(m => m.GetByIdWithOwnerAsync(_folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFolder(_folderId, _otherUserId));

        var act = () => _service.UpdateAsync(
            _userId, _folderId, new UpdateFolderRequest("x", "#000", "i", 1));

        await act.Should().ThrowAsync<ForbiddenException>();
        _folderRepo.Verify(m => m.Update(It.IsAny<Folder>()), Times.Never);
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── DeleteAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_Owner_RemovesFolder()
    {
        var folder = MakeFolder(_folderId, _userId);
        _folderRepo.Setup(m => m.GetByIdWithOwnerAsync(_folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folder);

        await _service.DeleteAsync(_userId, _folderId);

        // Hard delete (CLAUDE.md: không dùng soft delete).
        _folderRepo.Verify(m => m.Remove(folder), Times.Once);
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_FolderNotFound_Throws404()
    {
        _folderRepo.Setup(m => m.GetByIdWithOwnerAsync(_folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Folder?)null);

        var act = () => _service.DeleteAsync(_userId, _folderId);

        await act.Should().ThrowAsync<NotFoundException>();
        _folderRepo.Verify(m => m.Remove(It.IsAny<Folder>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_NotOwner_Throws403()
    {
        _folderRepo.Setup(m => m.GetByIdWithOwnerAsync(_folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFolder(_folderId, _otherUserId));

        var act = () => _service.DeleteAsync(_userId, _folderId);

        await act.Should().ThrowAsync<ForbiddenException>();
        _folderRepo.Verify(m => m.Remove(It.IsAny<Folder>()), Times.Never);
    }

    // ── AddItemToFolderAsync ────────────────────────────────────────────────

    [Fact]
    public async Task AddItemToFolderAsync_Owner_CreatesJunctionAtNextPosition()
    {
        var itemId = Guid.NewGuid();
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _itemRepo.Setup(m => m.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeItem(itemId, _userId));
        _folderRepo.Setup(m => m.ItemFolderExistsAsync(itemId, _folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _folderRepo.Setup(m => m.GetMaxItemPositionAsync(_folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
        ItemFolder? added = null;
        _folderRepo.Setup(m => m.AddItemFolderAsync(It.IsAny<ItemFolder>(), It.IsAny<CancellationToken>()))
            .Callback((ItemFolder f, CancellationToken _) => added = f)
            .Returns(Task.CompletedTask);

        var result = await _service.AddItemToFolderAsync(
            _userId, _folderId, new AddItemToFolderRequest(itemId));

        added.Should().NotBeNull();
        added!.Position.Should().Be(5);
        result.ItemId.Should().Be(itemId);
        result.FolderId.Should().Be(_folderId);
        result.Position.Should().Be(5);
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddItemToFolderAsync_NotOwner_Throws403WithFolderOwnerOnlyCode()
    {
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => _service.AddItemToFolderAsync(
            _userId, _folderId, new AddItemToFolderRequest(Guid.NewGuid()));

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be(ErrorCodes.FolderOwnerOnly);
        _folderRepo.Verify(m => m.AddItemFolderAsync(It.IsAny<ItemFolder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddItemToFolderAsync_ItemNotOwned_Throws403WithItemNotOwnedCode()
    {
        var itemId = Guid.NewGuid();
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _itemRepo.Setup(m => m.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);

        var act = () => _service.AddItemToFolderAsync(
            _userId, _folderId, new AddItemToFolderRequest(itemId));

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be(ErrorCodes.ItemNotOwned);
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddItemToFolderAsync_AlreadyInFolder_Throws409()
    {
        var itemId = Guid.NewGuid();
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _itemRepo.Setup(m => m.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeItem(itemId, _userId));
        _folderRepo.Setup(m => m.ItemFolderExistsAsync(itemId, _folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var act = () => _service.AddItemToFolderAsync(
            _userId, _folderId, new AddItemToFolderRequest(itemId));

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be(ErrorCodes.ItemAlreadyInFolder);
        _folderRepo.Verify(m => m.AddItemFolderAsync(It.IsAny<ItemFolder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── AddItemsToFolderAsync (bulk) ────────────────────────────────────────

    [Fact]
    public async Task AddItemsToFolderAsync_SkipsExisting_AndNumbersPositionsSequentially()
    {
        var existingId = Guid.NewGuid();
        var newId1 = Guid.NewGuid();
        var newId2 = Guid.NewGuid();
        var ids = new List<Guid> { existingId, newId1, newId2 };

        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _itemRepo.Setup(m => m.GetByIdsAndUserAsync(It.IsAny<IEnumerable<Guid>>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids.Select(id => MakeItem(id, _userId)).ToList());
        _folderRepo.Setup(m => m.GetItemFoldersAsync(It.IsAny<IEnumerable<Guid>>(), _folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemFolder> { new() { ItemId = existingId, FolderId = _folderId, Position = 3 } });
        _folderRepo.Setup(m => m.GetMaxItemPositionAsync(_folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);
        List<ItemFolder>? added = null;
        _folderRepo.Setup(m => m.AddItemsFolderAsync(It.IsAny<IEnumerable<ItemFolder>>(), It.IsAny<CancellationToken>()))
            .Callback((IEnumerable<ItemFolder> f, CancellationToken _) => added = f.ToList())
            .Returns(Task.CompletedTask);

        await _service.AddItemsToFolderAsync(_userId, _folderId, new AddItemsToFolderBulkRequest(ids));

        added.Should().NotBeNull();
        added!.Select(f => f.ItemId).Should().BeEquivalentTo(new[] { newId1, newId2 });
        added.Select(f => f.Position).Should().BeEquivalentTo(new[] { 11, 12 });
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddItemsToFolderAsync_AllAlreadyInFolder_SavesWithoutInsert()
    {
        var id = Guid.NewGuid();
        var ids = new List<Guid> { id };

        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _itemRepo.Setup(m => m.GetByIdsAndUserAsync(It.IsAny<IEnumerable<Guid>>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Item> { MakeItem(id, _userId) });
        _folderRepo.Setup(m => m.GetItemFoldersAsync(It.IsAny<IEnumerable<Guid>>(), _folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemFolder> { new() { ItemId = id, FolderId = _folderId } });
        _folderRepo.Setup(m => m.GetMaxItemPositionAsync(_folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        await _service.AddItemsToFolderAsync(_userId, _folderId, new AddItemsToFolderBulkRequest(ids));

        _folderRepo.Verify(m => m.AddItemsFolderAsync(It.IsAny<IEnumerable<ItemFolder>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddItemsToFolderAsync_NotOwner_Throws403()
    {
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => _service.AddItemsToFolderAsync(
            _userId, _folderId, new AddItemsToFolderBulkRequest(new List<Guid> { Guid.NewGuid() }));

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be(ErrorCodes.FolderOwnerOnly);
    }

    [Fact]
    public async Task AddItemsToFolderAsync_OneItemNotOwned_Throws403AndAddsNothing()
    {
        var mine = Guid.NewGuid();
        var notMine = Guid.NewGuid();

        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        // Repo chỉ trả item thuộc user → 1 id bị thiếu ⇒ chặn cả lô (all-or-nothing).
        _itemRepo.Setup(m => m.GetByIdsAndUserAsync(It.IsAny<IEnumerable<Guid>>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Item> { MakeItem(mine, _userId) });

        var act = () => _service.AddItemsToFolderAsync(
            _userId, _folderId, new AddItemsToFolderBulkRequest(new List<Guid> { mine, notMine }));

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be(ErrorCodes.ItemsNotOwned);
        _folderRepo.Verify(m => m.AddItemsFolderAsync(It.IsAny<IEnumerable<ItemFolder>>(), It.IsAny<CancellationToken>()), Times.Never);
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── RemoveItemFromFolderAsync ───────────────────────────────────────────

    [Fact]
    public async Task RemoveItemFromFolderAsync_Owner_RemovesJunction()
    {
        var itemId = Guid.NewGuid();
        var junction = new ItemFolder { ItemId = itemId, FolderId = _folderId, Position = 1 };
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _folderRepo.Setup(m => m.GetItemFolderAsync(itemId, _folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(junction);

        await _service.RemoveItemFromFolderAsync(_userId, _folderId, itemId);

        _folderRepo.Verify(m => m.RemoveItemFolder(junction), Times.Once);
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveItemFromFolderAsync_NotOwner_Throws403()
    {
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => _service.RemoveItemFromFolderAsync(_userId, _folderId, Guid.NewGuid());

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be(ErrorCodes.FolderOwnerOnly);
        _folderRepo.Verify(m => m.RemoveItemFolder(It.IsAny<ItemFolder>()), Times.Never);
    }

    [Fact]
    public async Task RemoveItemFromFolderAsync_ItemNotInFolder_Throws404()
    {
        var itemId = Guid.NewGuid();
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _folderRepo.Setup(m => m.GetItemFolderAsync(itemId, _folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemFolder?)null);

        var act = () => _service.RemoveItemFromFolderAsync(_userId, _folderId, itemId);

        await act.Should().ThrowAsync<NotFoundException>();
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── RemoveItemsFromFolderAsync (bulk) ───────────────────────────────────

    [Fact]
    public async Task RemoveItemsFromFolderAsync_Owner_RemovesFoundJunctions()
    {
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var junctions = ids.Select(id => new ItemFolder { ItemId = id, FolderId = _folderId }).ToList();
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _folderRepo.Setup(m => m.GetItemFoldersAsync(It.IsAny<IEnumerable<Guid>>(), _folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(junctions);

        await _service.RemoveItemsFromFolderAsync(
            _userId, _folderId, new RemoveItemsFromFolderBulkRequest(ids));

        _folderRepo.Verify(m => m.RemoveItemsFolder(It.IsAny<IEnumerable<ItemFolder>>()), Times.Once);
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveItemsFromFolderAsync_NothingMatches_DoesNotSave()
    {
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _folderRepo.Setup(m => m.GetItemFoldersAsync(It.IsAny<IEnumerable<Guid>>(), _folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemFolder>());

        await _service.RemoveItemsFromFolderAsync(
            _userId, _folderId, new RemoveItemsFromFolderBulkRequest(new List<Guid> { Guid.NewGuid() }));

        _folderRepo.Verify(m => m.RemoveItemsFolder(It.IsAny<IEnumerable<ItemFolder>>()), Times.Never);
        _folderRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveItemsFromFolderAsync_NotOwner_Throws403()
    {
        _folderRepo.Setup(m => m.ExistsByOwnerAsync(_folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => _service.RemoveItemsFromFolderAsync(
            _userId, _folderId, new RemoveItemsFromFolderBulkRequest(new List<Guid> { Guid.NewGuid() }));

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be(ErrorCodes.FolderOwnerOnly);
        _folderRepo.Verify(m => m.RemoveItemsFolder(It.IsAny<IEnumerable<ItemFolder>>()), Times.Never);
    }
}
