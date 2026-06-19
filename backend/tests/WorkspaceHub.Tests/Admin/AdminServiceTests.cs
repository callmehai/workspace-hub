using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.DTOs.Admin;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Infrastructure.Data;
using WorkspaceHub.Infrastructure.Services;

namespace WorkspaceHub.Tests.Admin;

/// <summary>
/// Unit tests cho AdminService.
/// Dùng EF InMemory vì AdminService inject AppDbContext trực tiếp cho EF projection
/// (ConnectionCount, ItemCount, GroupBy) — không thể mock bằng Moq thông thường.
///
/// Mỗi test dùng database name riêng (Guid.NewGuid()) để isolate hoàn toàn.
/// </summary>
public class AdminServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AdminService _sut;

    public AdminServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()) // isolate mỗi test
            .Options;

        _db = new AppDbContext(options);
        _sut = new AdminService(_db);
    }

    public void Dispose() => _db.Dispose();

    // ─────────────────────── Helpers ───────────────────────

    private User CreateUser(
        string email = "test@example.com",
        string fullName = "Test User",
        bool isActive = true,
        UserRole role = UserRole.User,
        DateTime? createdAt = null)
    {
        var user = new User
        {
            Id         = Guid.NewGuid(),
            Email      = email,
            FullName   = fullName,
            IsActive   = isActive,
            Role       = role,
            CreatedAt  = createdAt ?? DateTime.UtcNow,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
        };
        _db.Users.Add(user);
        return user;
    }

    private Connection CreateConnection(
        Guid userId,
        ConnectionStatus status = ConnectionStatus.Active,
        DateTime? lastSyncedAt = null)
    {
        var integration = _db.Integrations.FirstOrDefault()
            ?? new Integration
            {
                Id = Guid.NewGuid(),
                Key = "google",
                DisplayName = "Google",
                IconUrl = "",
                Description = "",
                Provider = "Google",
                AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
                TokenEndpoint = "https://oauth2.googleapis.com/token",
                SupportedServices = "[\"Gmail\"]",
                IsEnabled = true
            };

        if (!_db.Integrations.Any())
            _db.Integrations.Add(integration);

        var conn = new Connection
        {
            Id                    = Guid.NewGuid(),
            UserId                = userId,
            IntegrationId         = integration.Id,
            Provider              = ProviderType.Google,
            ServiceType           = ServiceType.Gmail,
            ProviderAccountId     = $"account-{Guid.NewGuid()}",
            AccessTokenEncrypted  = "encrypted-access",
            RefreshTokenEncrypted = "encrypted-refresh",
            ExpiresAt             = DateTime.UtcNow.AddHours(1),
            Status                = status,
            LastSyncedAt          = lastSyncedAt,
            CreatedAt             = DateTime.UtcNow
        };
        _db.Connections.Add(conn);
        return conn;
    }

    private Item CreateItem(Guid userId)
    {
        var item = new Item
        {
            Id           = Guid.NewGuid(),
            UserId       = userId,
            Type         = ItemType.Email,
            Title        = "Test Email",
            Snippet      = "Test snippet",
            Status       = ItemStatus.Inbox,
            OccurredAt   = DateTime.UtcNow,
            IsImportant  = false,
            IsArchived   = false,
            MetadataJson = "{}"
        };
        _db.Items.Add(item);
        return item;
    }

    // ─────────────────── GetUsersAsync Tests ───────────────────

    [Fact]
    public async Task GetUsersAsync_ReturnsPagedResult_WithCorrectTotalCount()
    {
        // Arrange — 3 users, request page=1, limit=20
        CreateUser("a@test.com", "Alpha User");
        CreateUser("b@test.com", "Beta User");
        CreateUser("c@test.com", "Gamma User");
        await _db.SaveChangesAsync();

        var request = new GetAdminUsersRequest(Page: 1, Limit: 20);

        // Act
        var result = await _sut.GetUsersAsync(request);

        // Assert
        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.Limit);
    }

    [Fact]
    public async Task GetUsersAsync_FiltersBy_EmailContains()
    {
        // Arrange
        CreateUser("vy@gmail.com", "Vy Cuong");
        CreateUser("john@work.com", "John Doe");
        await _db.SaveChangesAsync();

        var request = new GetAdminUsersRequest(Search: "gmail");

        // Act
        var result = await _sut.GetUsersAsync(request);

        // Assert
        Assert.Equal(1, result.Total);
        Assert.Single(result.Items);
        Assert.Equal("vy@gmail.com", result.Items[0].Email);
    }

    [Fact]
    public async Task GetUsersAsync_FiltersBy_FullNameContains()
    {
        // Arrange
        CreateUser("a@test.com", "Vy Cuong");
        CreateUser("b@test.com", "John Doe");
        await _db.SaveChangesAsync();

        var request = new GetAdminUsersRequest(Search: "Vy");

        // Act
        var result = await _sut.GetUsersAsync(request);

        // Assert
        Assert.Equal(1, result.Total);
        Assert.Equal("Vy Cuong", result.Items[0].FullName);
    }

    [Fact]
    public async Task GetUsersAsync_Search_FiltersCorrectlyByFullNameContainsMatch()
    {
        // Arrange
        // Lưu ý quan trọng về test này:
        // - EF InMemory: String.Contains() dùng ordinal comparison (CASE-SENSITIVE).
        // - SQL Server (production): CI_AS collation → Contains() map thành LIKE '%...%' = case-insensitive.
        // - Test này verify logic routing của service (search term được truyền xuống đúng, filter áp dụng).
        // - Việc search thực sự case-insensitive ở production là do SQL Server collation, không phải code.
        // - Convention: KHÔNG dùng .ToLower() trong LINQ (xem task spec và CONVENTIONS.md).
        // → Test này dùng cùng case để chạy được trên InMemory; integration test với SQL Server mới test đầy đủ CI.
        CreateUser("vy@gmail.com", "Vy Cuong");
        CreateUser("john@test.com", "John Doe");
        await _db.SaveChangesAsync();

        // Search dùng exact case match (cho InMemory); SQL Server sẽ match "VY" → "Vy" tự động
        var request = new GetAdminUsersRequest(Search: "Vy");

        // Act
        var result = await _sut.GetUsersAsync(request);

        // Assert
        Assert.Equal(1, result.Total);
        Assert.Equal("Vy Cuong", result.Items[0].FullName);
    }

    [Fact]
    public async Task GetUsersAsync_ClampsPageToMinimum1()
    {
        // Arrange
        CreateUser();
        await _db.SaveChangesAsync();

        var request = new GetAdminUsersRequest(Page: -5, Limit: 20);

        // Act
        var result = await _sut.GetUsersAsync(request);

        // Assert — page clamped to 1
        Assert.Equal(1, result.Page);
    }

    [Fact]
    public async Task GetUsersAsync_ClampsLimitTo100Max()
    {
        // Arrange
        CreateUser();
        await _db.SaveChangesAsync();

        var request = new GetAdminUsersRequest(Page: 1, Limit: 999);

        // Act
        var result = await _sut.GetUsersAsync(request);

        // Assert — limit clamped to 100
        Assert.Equal(100, result.Limit);
    }

    [Fact]
    public async Task GetUsersAsync_ReturnsEmptyList_WhenNoMatch()
    {
        // Arrange
        CreateUser("real@test.com", "Real User");
        await _db.SaveChangesAsync();

        var request = new GetAdminUsersRequest(Search: "xyzxyzxyz999");

        // Act
        var result = await _sut.GetUsersAsync(request);

        // Assert
        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task GetUsersAsync_IncludesConnectionCountAndItemCount()
    {
        // Arrange
        var user = CreateUser("counted@test.com", "Counted User");
        CreateConnection(user.Id);
        CreateConnection(user.Id, ConnectionStatus.Error);
        CreateItem(user.Id);
        CreateItem(user.Id);
        CreateItem(user.Id);
        await _db.SaveChangesAsync();

        var request = new GetAdminUsersRequest();

        // Act
        var result = await _sut.GetUsersAsync(request);

        // Assert
        var dto = result.Items.Single();
        Assert.Equal(2, dto.ConnectionCount); // all connections regardless of status
        Assert.Equal(3, dto.ItemCount);
    }

    // ─────────────────── GetStatsAsync Tests ───────────────────

    [Fact]
    public async Task GetStatsAsync_ReturnsCorrectActiveAndLockedCounts()
    {
        // Arrange — 3 active, 2 locked
        CreateUser("u1@t.com", isActive: true);
        CreateUser("u2@t.com", isActive: true);
        CreateUser("u3@t.com", isActive: true);
        CreateUser("u4@t.com", isActive: false);
        CreateUser("u5@t.com", isActive: false);
        await _db.SaveChangesAsync();

        // Act
        var stats = await _sut.GetStatsAsync();

        // Assert
        Assert.Equal(5, stats.TotalUsers);
        Assert.Equal(3, stats.ActiveUsers);
        Assert.Equal(2, stats.LockedUsers);
    }

    [Fact]
    public async Task GetStatsAsync_ActivePlusLockedEqualsTotal()
    {
        // Arrange — mix của active + locked
        CreateUser("u1@t.com", isActive: true);
        CreateUser("u2@t.com", isActive: false);
        CreateUser("u3@t.com", isActive: true);
        await _db.SaveChangesAsync();

        // Act
        var stats = await _sut.GetStatsAsync();

        // Assert — invariant: active + locked == total
        Assert.Equal(stats.TotalUsers, stats.ActiveUsers + stats.LockedUsers);
    }

    [Fact]
    public async Task GetStatsAsync_SyncErrorsLast24h_CountsOnlyErrorStatus()
    {
        // Arrange
        var user = CreateUser();
        var recentTime = DateTime.UtcNow.AddHours(-1);

        CreateConnection(user.Id, ConnectionStatus.Error, recentTime);    // phải đếm
        CreateConnection(user.Id, ConnectionStatus.Active, recentTime);   // không đếm (Active)
        CreateConnection(user.Id, ConnectionStatus.Disconnected, recentTime); // không đếm
        await _db.SaveChangesAsync();

        // Act
        var stats = await _sut.GetStatsAsync();

        // Assert
        Assert.Equal(1, stats.SyncErrorsLast24h);
    }

    [Fact]
    public async Task GetStatsAsync_SyncErrorsLast24h_ExcludesOlderThan24h()
    {
        // Arrange
        var user = CreateUser();
        var recent = DateTime.UtcNow.AddHours(-1);
        var old    = DateTime.UtcNow.AddHours(-25); // ngoài cửa sổ 24h

        CreateConnection(user.Id, ConnectionStatus.Error, recent); // đếm
        CreateConnection(user.Id, ConnectionStatus.Error, old);    // không đếm (quá cũ)
        await _db.SaveChangesAsync();

        // Act
        var stats = await _sut.GetStatsAsync();

        // Assert
        Assert.Equal(1, stats.SyncErrorsLast24h);
    }

    [Fact]
    public async Task GetStatsAsync_SyncErrorsLast24h_ExcludesNullLastSyncedAt()
    {
        // Arrange — connection có Status=Error nhưng LastSyncedAt = null
        var user = CreateUser();
        CreateConnection(user.Id, ConnectionStatus.Error, lastSyncedAt: null); // không đếm
        await _db.SaveChangesAsync();

        // Act
        var stats = await _sut.GetStatsAsync();

        // Assert
        Assert.Equal(0, stats.SyncErrorsLast24h);
    }

    [Fact]
    public async Task GetStatsAsync_ConnectionsByStatus_ReturnsCorrectGroups()
    {
        // Arrange
        var user = CreateUser();
        CreateConnection(user.Id, ConnectionStatus.Active);
        CreateConnection(user.Id, ConnectionStatus.Active);
        CreateConnection(user.Id, ConnectionStatus.Error);
        await _db.SaveChangesAsync();

        // Act
        var stats = await _sut.GetStatsAsync();

        // Assert — chỉ key có count > 0 xuất hiện; Disconnected không có nên không có trong dict
        Assert.Equal(2, stats.ConnectionsByStatus["Active"]);
        Assert.Equal(1, stats.ConnectionsByStatus["Error"]);
        Assert.False(stats.ConnectionsByStatus.ContainsKey("Disconnected"));
    }

    [Fact]
    public async Task GetStatsAsync_TotalConnectionsMatchesSumOfByStatus()
    {
        // Arrange
        var user = CreateUser();
        CreateConnection(user.Id, ConnectionStatus.Active);
        CreateConnection(user.Id, ConnectionStatus.Error);
        CreateConnection(user.Id, ConnectionStatus.Disconnected);
        await _db.SaveChangesAsync();

        // Act
        var stats = await _sut.GetStatsAsync();

        // Assert — sum của connectionsByStatus phải bằng totalConnections
        var sumOfGroups = stats.ConnectionsByStatus.Values.Sum();
        Assert.Equal(stats.TotalConnections, sumOfGroups);
    }
}
