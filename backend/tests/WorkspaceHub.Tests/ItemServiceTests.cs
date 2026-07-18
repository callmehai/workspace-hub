using Microsoft.Extensions.Logging;
using Moq;
using System.Linq;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Tests;

/// <summary>
/// Unit tests cho ItemService — test business logic mapping, clamping, delegation.
/// Mock IItemRepository để isolate service layer.
/// </summary>
public class ItemServiceTests
{
    private readonly Mock<IItemRepository> _repoMock;
    private readonly Mock<IFolderRepository> _folderRepoMock;
    private readonly Mock<IConnectionHealthChecker> _healthCheckerMock;
    private readonly Mock<ILogger<ItemService>> _loggerMock;
    private readonly ItemService _sut; // System Under Test
    private readonly Guid _userId = Guid.NewGuid();

    public ItemServiceTests()
    {
        _repoMock = new Mock<IItemRepository>();
        _folderRepoMock = new Mock<IFolderRepository>();
        _healthCheckerMock = new Mock<IConnectionHealthChecker>();
        _loggerMock = new Mock<ILogger<ItemService>>();
        _sut = new ItemService(_repoMock.Object, _folderRepoMock.Object, _healthCheckerMock.Object, _loggerMock.Object);
    }

    // ───────────── Helper ─────────────

    private static Item CreateItem(
        Guid userId,
        ItemType type = ItemType.Email,
        ItemStatus status = ItemStatus.Inbox,
        bool isImportant = false,
        string title = "Test Item",
        string snippet = "Test snippet")
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Title = title,
            Snippet = snippet,
            Status = status,
            OccurredAt = DateTime.UtcNow,
            IsImportant = isImportant,
            IsArchived = false,
            MetadataJson = "{}"
        };

    // ───────────── Tests ─────────────

    [Fact]
    public async Task GetItemsAsync_ReturnsCorrectPagedResult()
    {
        // Arrange
        var items = new List<Item>
        {
            CreateItem(_userId, title: "Email 1"),
            CreateItem(_userId, title: "Email 2")
        };

        _repoMock
            .Setup(r => r.GetPagedAsync(
                _userId, null, null, null, null, null, null, null, null, null, null, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((items.AsReadOnly(), 2, new Dictionary<string, int>()));

        var request = new GetItemsRequest();

        // Act
        var result = await _sut.GetItemsAsync(_userId, request);

        // Assert
        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.Limit);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Email 1", result.Items[0].Title);
        Assert.Equal("Email 2", result.Items[1].Title);
    }

    [Fact]
    public async Task GetItemsAsync_ClampsPageToMinimum1()
    {
        // Arrange — page = -5 phải được clamp thành 1
        _repoMock
            .Setup(r => r.GetPagedAsync(
                _userId, null, null, null, null, null, null, null, null, null, null, null, null,
                1, // clamp thành 1
                20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Item>().AsReadOnly(), 0, new Dictionary<string, int>()));

        var request = new GetItemsRequest(Page: -5);

        // Act
        var result = await _sut.GetItemsAsync(_userId, request);

        // Assert
        Assert.Equal(1, result.Page);
        _repoMock.Verify(r => r.GetPagedAsync(
            _userId, null, null, null, null, null, null, null, null, null, null, null, null,
            1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetItemsAsync_ClampsLimitTo100Max()
    {
        // Arrange — limit = 999 phải được clamp thành 100
        _repoMock
            .Setup(r => r.GetPagedAsync(
                _userId, null, null, null, null, null, null, null, null, null, null, null, null,
                1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Item>().AsReadOnly(), 0, new Dictionary<string, int>()));

        var request = new GetItemsRequest(Limit: 999);

        // Act
        var result = await _sut.GetItemsAsync(_userId, request);

        // Assert
        Assert.Equal(100, result.Limit);
        _repoMock.Verify(r => r.GetPagedAsync(
            _userId, null, null, null, null, null, null, null, null, null, null, null, null,
            1, 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetItemsAsync_ClampsLimitTo1Min()
    {
        // Arrange — limit = 0 phải được clamp thành 1
        _repoMock
            .Setup(r => r.GetPagedAsync(
                _userId, null, null, null, null, null, null, null, null, null, null, null, null,
                1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Item>().AsReadOnly(), 0, new Dictionary<string, int>()));

        var request = new GetItemsRequest(Limit: 0);

        // Act
        var result = await _sut.GetItemsAsync(_userId, request);

        // Assert
        Assert.Equal(1, result.Limit);
    }

    [Fact]
    public async Task GetItemsAsync_TrimsSearchTerm()
    {
        // Arrange — search = "  meeting  " phải được trim thành "meeting"
        _repoMock
            .Setup(r => r.GetPagedAsync(
                _userId, null, null, null, null,
                "meeting", null, null, null, null, null, null, null, // trimmed
                1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Item>().AsReadOnly(), 0, new Dictionary<string, int>()));

        var request = new GetItemsRequest(Search: "  meeting  ");

        // Act
        await _sut.GetItemsAsync(_userId, request);

        // Assert
        _repoMock.Verify(r => r.GetPagedAsync(
            _userId, null, null, null, null,
            "meeting", null, null, null, null, null, null, null, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetItemsAsync_PassesAllFiltersToRepository()
    {
        // Arrange
        var folderId = Guid.NewGuid();

        _folderRepoMock
            .Setup(r => r.ExistsByOwnerAsync(folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _repoMock
            .Setup(r => r.GetPagedAsync(
                _userId, folderId,
                It.Is<IReadOnlyList<ItemStatus>>(s => s.SequenceEqual(new[] { ItemStatus.Doing })),
                It.Is<IReadOnlyList<ItemType>>(ty => ty.SequenceEqual(new[] { ItemType.Email })),
                true, "report", null, null, null, null, null, null, null, 2, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Item>().AsReadOnly(), 0, new Dictionary<string, int>()));

        var request = new GetItemsRequest(
            FolderId: folderId,
            Statuses: new[] { ItemStatus.Doing },
            Types: new[] { ItemType.Email },
            IsImportant: true,
            Search: "report",
            Page: 2,
            Limit: 10);

        // Act
        await _sut.GetItemsAsync(_userId, request);

        // Assert — verify tất cả params đều được truyền đúng xuống repository
        _repoMock.Verify(r => r.GetPagedAsync(
            _userId, folderId,
            It.Is<IReadOnlyList<ItemStatus>>(s => s.SequenceEqual(new[] { ItemStatus.Doing })),
            It.Is<IReadOnlyList<ItemType>>(ty => ty.SequenceEqual(new[] { ItemType.Email })),
            true, "report", null, null, null, null, null, null, null, 2, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetItemsAsync_MapsEntityToResponseDto()
    {
        // Arrange
        var item = CreateItem(_userId,
            type: ItemType.Note,
            status: ItemStatus.Done,
            isImportant: true,
            title: "My Note",
            snippet: "Note content preview");
        item.DueAt = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        item.ExternalId = "ext-123";

        _repoMock
            .Setup(r => r.GetPagedAsync(
                _userId, null, null, null, null, null, null, null, null, null, null, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Item> { item }.AsReadOnly(), 1, new Dictionary<string, int>()));

        // Act
        var result = await _sut.GetItemsAsync(_userId, new GetItemsRequest());

        // Assert — verify mapping chính xác từ entity → DTO
        var dto = result.Items[0];
        Assert.Equal(item.Id, dto.Id);
        Assert.Equal(ItemType.Note, dto.Type);
        Assert.Equal("My Note", dto.Title);
        Assert.Equal("Note content preview", dto.Snippet);
        Assert.Equal(ItemStatus.Done, dto.Status);
        Assert.True(dto.IsImportant);
        Assert.Equal("ext-123", dto.ExternalId);
        Assert.Equal(new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc), dto.DueAt);
    }

    [Fact]
    public async Task GetItemsAsync_EmptyResult_ReturnsEmptyList()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetPagedAsync(
                _userId, null, null, null, null, null, null, null, null, null, null, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Item>().AsReadOnly(), 0, new Dictionary<string, int>()));

        // Act
        var result = await _sut.GetItemsAsync(_userId, new GetItemsRequest());

        // Assert
        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task GetItemsAsync_FolderIdBelongsToAnotherUser_ReturnsEmptyResult()
    {
        // Arrange
        var folderId = Guid.NewGuid();

        _folderRepoMock
            .Setup(r => r.ExistsByOwnerAsync(folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var request = new GetItemsRequest(FolderId: folderId);

        // Act
        var result = await _sut.GetItemsAsync(_userId, request);

        // Assert
        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
        _repoMock.Verify(r => r.GetPagedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<ItemStatus>?>(),
            It.IsAny<IReadOnlyList<ItemType>?>(), It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<IReadOnlyList<Guid>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetItemsAsync_FolderIdNotFound_ReturnsEmptyResult()
    {
        // Arrange
        var folderId = Guid.NewGuid();

        _folderRepoMock
            .Setup(r => r.ExistsByOwnerAsync(folderId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var request = new GetItemsRequest(FolderId: folderId);

        // Act
        var result = await _sut.GetItemsAsync(_userId, request);

        // Assert
        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
        _repoMock.Verify(r => r.GetPagedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyList<ItemStatus>?>(),
            It.IsAny<IReadOnlyList<ItemType>?>(), It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<IReadOnlyList<Guid>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetItemByIdAsync_ReturnsMappedResponse_WhenItemExistsAndBelongsToUser()
    {
        // Arrange
        var item = CreateItem(_userId, title: "Test Item");
        _repoMock
            .Setup(r => r.GetByIdAndUserAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        var result = await _sut.GetItemByIdAsync(_userId, item.Id);

        // Assert
        Assert.Equal(item.Id, result.Id);
        Assert.Equal("Test Item", result.Title);
    }

    [Fact]
    public async Task GetItemByIdAsync_ThrowsNotFoundException_WhenItemDoesNotExistOrDoesNotBelongToUser()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        _repoMock
            .Setup(r => r.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);

        // Act & Assert
        await Assert.ThrowsAsync<WorkspaceHub.Application.Common.NotFoundException>(() =>
            _sut.GetItemByIdAsync(_userId, itemId));
    }

    // ───────────── Phân quyền folder chia sẻ (shared folder) ─────────────
    // Phần rủi ro nhất của feature: item thuộc owner A nhưng user B thao tác qua folder chia sẻ.

    /// <summary>Editor của folder chia sẻ ĐƯỢC đổi trạng thái item của người khác.</summary>
    [Fact]
    public async Task UpdateStatusAsync_SharedEditor_UpdatesItemOfAnotherUser()
    {
        var ownerId = Guid.NewGuid();
        var item = CreateItem(ownerId);

        // B không sở hữu item → lookup theo user trả null.
        _repoMock.Setup(r => r.GetByIdAndUserAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);
        _folderRepoMock.Setup(f => f.IsItemSharedWithUserAsEditorAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repoMock.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        var result = await _sut.UpdateStatusAsync(_userId, item.Id, new UpdateItemStatusRequest(ItemStatus.Done));

        Assert.Equal(ItemStatus.Done, result.Status);
        // Item của người khác → IsOwner phải là false để FE ẩn hành động chỉ owner làm được.
        Assert.False(result.IsOwner);
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Viewer (chỉ xem) thao tác GHI → 403 ForbiddenException, KHÔNG phải 404 lộ GUID.</summary>
    [Fact]
    public async Task UpdateStatusAsync_SharedViewer_ThrowsForbidden()
    {
        var itemId = Guid.NewGuid();

        _repoMock.Setup(r => r.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);
        _folderRepoMock.Setup(f => f.IsItemSharedWithUserAsEditorAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        // Có quyền XEM (item nằm trong folder được chia sẻ) nhưng không phải Editor.
        _folderRepoMock.Setup(f => f.IsItemSharedWithUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<WorkspaceHub.Application.Common.ForbiddenException>(() =>
            _sut.UpdateStatusAsync(_userId, itemId, new UpdateItemStatusRequest(ItemStatus.Done)));
    }

    /// <summary>Không liên quan gì tới item → vẫn 404 (không lộ sự tồn tại của item).</summary>
    [Fact]
    public async Task UpdateStatusAsync_NoAccess_ThrowsNotFound()
    {
        var itemId = Guid.NewGuid();

        _repoMock.Setup(r => r.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);
        _folderRepoMock.Setup(f => f.IsItemSharedWithUserAsEditorAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _folderRepoMock.Setup(f => f.IsItemSharedWithUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<WorkspaceHub.Application.Common.NotFoundException>(() =>
            _sut.UpdateStatusAsync(_userId, itemId, new UpdateItemStatusRequest(ItemStatus.Done)));
    }

    /// <summary>Editor ĐƯỢC đánh dấu quan trọng item của người khác (trước đây trả 404).</summary>
    [Fact]
    public async Task ToggleImportantAsync_SharedEditor_UpdatesItemOfAnotherUser()
    {
        var ownerId = Guid.NewGuid();
        var item = CreateItem(ownerId, isImportant: false);

        _repoMock.Setup(r => r.GetByIdAndUserAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);
        _folderRepoMock.Setup(f => f.IsItemSharedWithUserAsEditorAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repoMock.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        var result = await _sut.ToggleImportantAsync(_userId, item.Id, isImportant: true);

        Assert.True(result.IsImportant);
        Assert.False(result.IsOwner);
    }

    /// <summary>Viewer đánh dấu quan trọng → 403 (đồng bộ hợp đồng lỗi với UpdateStatus).</summary>
    [Fact]
    public async Task ToggleImportantAsync_SharedViewer_ThrowsForbidden()
    {
        var itemId = Guid.NewGuid();

        _repoMock.Setup(r => r.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);
        _folderRepoMock.Setup(f => f.IsItemSharedWithUserAsEditorAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _folderRepoMock.Setup(f => f.IsItemSharedWithUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<WorkspaceHub.Application.Common.ForbiddenException>(() =>
            _sut.ToggleImportantAsync(_userId, itemId, isImportant: true));
    }

    /// <summary>Item của chính mình → IsOwner = true (FE hiện nút mở trên provider).</summary>
    [Fact]
    public async Task GetItemByIdAsync_OwnItem_ReturnsIsOwnerTrue()
    {
        var item = CreateItem(_userId);
        _repoMock.Setup(r => r.GetByIdAndUserAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        var result = await _sut.GetItemByIdAsync(_userId, item.Id);

        Assert.True(result.IsOwner);
    }

    /// <summary>Viewer ĐƯỢC đọc item của người khác, và IsOwner = false.</summary>
    [Fact]
    public async Task GetItemByIdAsync_SharedViewer_ReturnsItemWithIsOwnerFalse()
    {
        var ownerId = Guid.NewGuid();
        var item = CreateItem(ownerId);

        _repoMock.Setup(r => r.GetByIdAndUserAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);
        _folderRepoMock.Setup(f => f.IsItemSharedWithUserAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repoMock.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        var result = await _sut.GetItemByIdAsync(_userId, item.Id);

        Assert.Equal(item.Id, result.Id);
        Assert.False(result.IsOwner);
    }
}
