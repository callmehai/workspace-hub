using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Infrastructure.Data;
using WorkspaceHub.Infrastructure.Repositories;
using Xunit;

namespace WorkspaceHub.Tests.Infrastructure;

/// <summary>
/// Test hành vi thực cho filter <c>driveKind</c> (Thư mục / Tệp) và sort folders-first của
/// <see cref="ItemRepository.GetPagedAsync"/> — chạy trên EF InMemory (không cần DB sống).
/// Bổ sung cho <see cref="ItemListQuerySqlTests"/> (chỉ verify SQL sinh ra).
/// </summary>
public class ItemRepositoryDriveFilterTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _connId = Guid.NewGuid();

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()) // isolate mỗi test
            .Options);

    private Item DriveItem(string title, string metadataJson, DateTime occurredAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = _userId,
        Type = ItemType.File,
        ConnectionId = _connId,
        ExternalId = $"ext-{title}",
        Title = title,
        Snippet = title,
        OccurredAt = occurredAt,
        MetadataJson = metadataJson,
    };

    /// <summary>3 item Drive (đều isTopLevel để lọt filter root): 1 folder + 1 file mới + 1 file "cũ" thiếu isFolder.</summary>
    private async Task<(AppDbContext Db, ItemRepository Repo, Guid FolderId, Guid FileNewId, Guid FileLegacyId)> SeedAsync()
    {
        var db = NewDb();
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var folder = DriveItem("Folder A", """{"isFolder":true,"isTopLevel":true}""", baseTime);
        var fileNew = DriveItem("new.png", """{"isFolder":false,"isTopLevel":true,"mimeType":"image/png"}""", baseTime.AddMinutes(10));
        // File "cũ" — metadata KHÔNG có field isFolder (item sync trước khi có field / metadata tối giản).
        var fileLegacy = DriveItem("legacy.pdf", """{"isTopLevel":true,"mimeType":"application/pdf"}""", baseTime.AddMinutes(5));

        db.Items.AddRange(folder, fileNew, fileLegacy);
        await db.SaveChangesAsync();

        return (db, new ItemRepository(db), folder.Id, fileNew.Id, fileLegacy.Id);
    }

    [Fact]
    public async Task GetPagedAsync_DriveKindFolder_ReturnsOnlyFolders()
    {
        var (db, repo, folderId, _, _) = await SeedAsync();
        using var _ = db;

        var (items, total, _) = await repo.GetPagedAsync(
            _userId, types: new[] { ItemType.File }, driveKind: "folder");

        total.Should().Be(1);
        items.Should().ContainSingle().Which.Id.Should().Be(folderId);
    }

    [Fact]
    public async Task GetPagedAsync_DriveKindFile_ReturnsFiles_IncludingLegacyMissingIsFolder()
    {
        var (db, repo, _, fileNewId, fileLegacyId) = await SeedAsync();
        using var _ = db;

        var (items, total, _) = await repo.GetPagedAsync(
            _userId, types: new[] { ItemType.File }, driveKind: "file");

        // Cả file mới lẫn file "cũ" (thiếu isFolder) đều phải xuất hiện — không bị giấu khỏi tab "Tệp".
        total.Should().Be(2);
        items.Select(i => i.Id).Should().BeEquivalentTo(new[] { fileNewId, fileLegacyId });
    }

    [Fact]
    public async Task GetPagedAsync_DriveScope_SortsFoldersFirst_ThenNewest()
    {
        var (db, repo, folderId, fileNewId, _) = await SeedAsync();
        using var _ = db;

        // Không lọc driveKind — nhưng types=[File] → view Drive → folder nổi lên đầu dù file mới hơn.
        var (items, _, _) = await repo.GetPagedAsync(_userId, types: new[] { ItemType.File });

        items.Should().HaveCount(3);
        items[0].Id.Should().Be(folderId);   // folder trước tiên
        items[1].Id.Should().Be(fileNewId);  // rồi tới file mới nhất
    }
}
