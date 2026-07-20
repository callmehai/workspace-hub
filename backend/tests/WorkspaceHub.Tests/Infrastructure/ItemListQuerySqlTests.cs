using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Infrastructure.Data;
using Xunit;

namespace WorkspaceHub.Tests.Infrastructure;

/// <summary>
/// Verify SQL sinh ra cho items list (SCRUM-25) mà KHÔNG cần DB sống — dùng <c>ToQueryString()</c>
/// (EF Core sinh T-SQL phía client, không mở kết nối). Mục tiêu chứng minh:
///   • filter + sort + paging được dịch xuống DB (không load hết rồi cắt in-memory);
///   • chỉ 1 câu SELECT duy nhất → không subquery tương quan lặp theo row → không N+1.
///
/// ⚠️ Query ở test này MIRROR <see cref="WorkspaceHub.Infrastructure.Repositories.ItemRepository"/>.GetPagedAsync —
/// đổi filter/sort ở repository thì cập nhật cả test này.
/// </summary>
public class ItemListQuerySqlTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public ItemListQuerySqlTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    /// <summary>Context cấu hình SqlServer chỉ để sinh SQL — ToQueryString KHÔNG mở connection.</summary>
    private static AppDbContext OfflineSqlServerContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=offline;Database=verify;Trusted_Connection=False;")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void ItemsPagedQuery_PagesAtDatabase_AsSingleSelect_NoNPlusOne()
    {
        using var db = OfflineSqlServerContext();
        var userId = Guid.NewGuid();
        const int page = 2, limit = 20;
        const string search = "report";

        // Mirror GetPagedAsync: user + !archived, các filter, search Title/Snippet, sort OccurredAt desc, paging.
        var sql = db.Set<Item>()
            .AsNoTracking()
            .Where(i => i.UserId == userId && !i.IsArchived)
            .Where(i => i.Status == ItemStatus.Inbox)
            .Where(i => i.Type == ItemType.Email)
            .Where(i => i.Title.Contains(search) || i.Snippet.Contains(search))
            .OrderByDescending(i => i.OccurredAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToQueryString();

        _output.WriteLine(sql); // in SQL thật để đối chiếu (đóng vai trò "đọc EF log" khi không có DB sống).

        // Paging dịch xuống DB (SQL Server OFFSET/FETCH) — không phải lọc/cắt in-memory.
        sql.Should().Contain("ORDER BY");
        sql.Should().Contain("OFFSET");
        sql.Should().Contain("FETCH NEXT");

        // Đúng 1 câu SELECT → không N+1 (không subquery tương quan lặp theo từng row).
        CountOccurrences(sql, "SELECT").Should().Be(1);
    }

    [Fact]
    public void ItemsPagedQuery_FolderFilter_TranslatesToExistsSubquery_NotInMemory()
    {
        using var db = OfflineSqlServerContext();
        var userId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        const int page = 1, limit = 20;

        // Mirror nhánh folderId của GetPagedAsync: join qua ItemFolders bằng .Any().
        var sql = db.Set<Item>()
            .AsNoTracking()
            .Where(i => i.UserId == userId && !i.IsArchived)
            .Where(i => i.ItemFolders.Any(ifj => ifj.FolderId == folderId))
            .OrderByDescending(i => i.OccurredAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToQueryString();

        _output.WriteLine(sql);

        // folderId filter dịch thành EXISTS subquery TRONG cùng 1 query (không load ItemFolders ra app
        // rồi lọc in-memory) — đây là nhánh dễ N+1 nhất nếu viết sai. EXISTS tương quan ≠ N+1.
        sql.Should().Contain("EXISTS");
        sql.Should().Contain("OFFSET");
        sql.Should().Contain("FETCH NEXT");
    }

    /// <summary>
    /// Filter ẩn item Drive con khi CHA của nó cũng nằm trong cùng workspace folder (bug "file con
    /// hiện 2 nơi"). Kiểm tra nó dịch được xuống SQL Server — LINQ hợp lệ trên EF InMemory vẫn có
    /// thể ném "could not be translated" trên provider thật.
    /// </summary>
    [Fact]
    public void ItemsPagedQuery_WorkspaceFolderHidesDriveChildren_TranslatesToSql()
    {
        using var db = OfflineSqlServerContext();
        var userId = Guid.NewGuid();
        var folderId = Guid.NewGuid();

        // Mirror nhánh "workspace folder, chưa duyệt vào thư mục Drive" của GetPagedAsync.
        var sql = db.Set<Item>()
            .AsNoTracking()
            .Where(i => i.UserId == userId && !i.IsArchived)
            .Where(i => i.ItemFolders.Any(ifj => ifj.FolderId == folderId))
            .Where(i =>
                i.Type != ItemType.File
                || i.MetadataJson == null
                || !db.Set<Item>().Any(p =>
                        p.Type == ItemType.File
                        && p.UserId == i.UserId
                        && !p.IsArchived
                        && p.ExternalId != null
                        && p.ItemFolders.Any(pf => pf.FolderId == folderId)
                        && i.MetadataJson.Contains("\"" + p.ExternalId + "\"")))
            .OrderByDescending(i => i.OccurredAt)
            .Take(20)
            .ToQueryString();

        _output.WriteLine(sql);

        // Dịch được xuống DB (không rơi về client-eval) — EXISTS lồng cho quan hệ cha-con Drive.
        sql.Should().Contain("EXISTS");
        sql.Should().NotContain("could not be translated");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    [Fact]
    public void ItemsPagedQuery_DriveHierarchyFilter_TranslatesToSql_Correctly()
    {
        using var db = OfflineSqlServerContext();
        var userId = Guid.NewGuid();
        
        // 1. Root / All Items -> filter isTopLevel
        var sqlRoot = db.Set<Item>()
            .AsNoTracking()
            .Where(i => i.UserId == userId && !i.IsArchived)
            .Where(i => i.Type != ItemType.File || (i.MetadataJson != null && i.MetadataJson.Contains("\"isTopLevel\":true")))
            .ToQueryString();

        sqlRoot.Should().Contain("\"isTopLevel\":true");

        // 2. Specific Drive Folder -> filter parentToken
        var sqlFolder = db.Set<Item>()
            .AsNoTracking()
            .Where(i => i.UserId == userId && !i.IsArchived)
            .Where(i => i.Type == ItemType.File && i.MetadataJson != null && i.MetadataJson.Contains("\"folder-id-123\""))
            .ToQueryString();

        sqlFolder.Should().Contain("\"folder-id-123\"");
    }
}
