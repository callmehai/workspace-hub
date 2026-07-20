using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Infrastructure.Data;
using WorkspaceHub.Infrastructure.Repositories;
using Xunit;

namespace WorkspaceHub.Tests.Infrastructure;

/// <summary>
/// Bug "file con hiện 2 nơi": user gán THƯ MỤC Drive vào workspace folder rồi upload file vào
/// thư mục đó trên Google Drive. Trước fix, khi mở workspace folder thì file con hiện NGANG HÀNG
/// với thư mục cha, đồng thời vẫn hiện khi duyệt vào trong thư mục → cùng một file ở hai chỗ.
///
/// Lỗi CHỈ xảy ra ở view workspace folder: nhánh lọc phân cấp cũ (isTopLevel) bị loại trừ khi
/// <c>folderId != null</c> nên không có filter nào ẩn con đi.
/// </summary>
public class ItemRepositoryWorkspaceFolderDriveTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _connId = Guid.NewGuid();
    private readonly Guid _folderId = Guid.NewGuid();

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private Item DriveItem(string title, string externalId, string metadataJson) => new()
    {
        Id = Guid.NewGuid(),
        UserId = _userId,
        Type = ItemType.File,
        ConnectionId = _connId,
        ExternalId = externalId,
        Title = title,
        Snippet = title,
        OccurredAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        MetadataJson = metadataJson,
    };

    /// <summary>
    /// "Test folder" (Drive) đã được gán vào workspace folder; "abc.txt" là con của nó trên Drive
    /// và KHÔNG được gán junction (đúng thiết kế — chỉ gán đúng item user chọn).
    /// </summary>
    private async Task<(AppDbContext Db, ItemRepository Repo, Guid DriveFolderId, Guid ChildFileId)> SeedAsync()
    {
        var db = NewDb();

        var driveFolder = DriveItem("Test folder", "drive-folder-1",
            """{"isFolder":true,"isTopLevel":true,"parents":["root-id"]}""");
        var childFile = DriveItem("abc.txt", "drive-file-1",
            """{"isFolder":false,"isTopLevel":false,"parents":["drive-folder-1"]}""");

        db.Items.AddRange(driveFolder, childFile);
        db.ItemFolders.Add(new ItemFolder
        {
            ItemId = driveFolder.Id,
            FolderId = _folderId,
            Position = 1,
            AddedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        return (db, new ItemRepository(db), driveFolder.Id, childFile.Id);
    }

    [Fact]
    public async Task GetPagedAsync_WorkspaceFolder_HidesDriveChildWhoseParentIsInSameFolder()
    {
        var (db, repo, driveFolderId, _) = await SeedAsync();
        using var _ = db;

        var (items, total, _) = await repo.GetPagedAsync(_userId, folderId: _folderId);

        // Chỉ thấy thư mục cha — abc.txt bị ẩn vì cha nó đã nằm trong chính folder này.
        total.Should().Be(1);
        items.Should().ContainSingle().Which.Id.Should().Be(driveFolderId);
    }

    [Fact]
    public async Task GetPagedAsync_BrowsingIntoDriveFolder_StillReturnsChild()
    {
        var (db, repo, _, childFileId) = await SeedAsync();
        using var _ = db;

        // Double-click vào "Test folder" → duyệt theo driveParentId, vẫn phải thấy abc.txt.
        var (items, total, _) = await repo.GetPagedAsync(
            _userId, folderId: _folderId, driveParentId: "drive-folder-1");

        total.Should().Be(1);
        items.Should().ContainSingle().Which.Id.Should().Be(childFileId);
    }

    [Fact]
    public async Task GetPagedAsync_WorkspaceFolder_KeepsChildWhenParentNotInThatFolder()
    {
        var (db, repo, driveFolderId, childFileId) = await SeedAsync();
        using var _ = db;

        // User gán THÊM chính abc.txt vào folder, nhưng gỡ thư mục cha ra.
        var parentJunction = await db.ItemFolders.FirstAsync(f => f.ItemId == driveFolderId);
        db.ItemFolders.Remove(parentJunction);
        db.ItemFolders.Add(new ItemFolder
        {
            ItemId = childFileId,
            FolderId = _folderId,
            Position = 2,
            AddedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var (items, total, _) = await repo.GetPagedAsync(_userId, folderId: _folderId);

        // Cha không còn trong folder → abc.txt là "gốc" theo góc nhìn folder này, phải hiện.
        total.Should().Be(1);
        items.Should().ContainSingle().Which.Id.Should().Be(childFileId);
    }

    [Fact]
    public async Task GetPagedAsync_AllItemsRoot_Unaffected()
    {
        var (db, repo, driveFolderId, _) = await SeedAsync();
        using var _ = db;

        // View All items (không folderId): lọc theo isTopLevel như cũ — chỉ thư mục cha.
        var (items, total, _) = await repo.GetPagedAsync(_userId);

        total.Should().Be(1);
        items.Should().ContainSingle().Which.Id.Should().Be(driveFolderId);
    }
}
