using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Tests.Controllers;

public class ConnectionsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ConnectionsControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(Microsoft.EntityFrameworkCore.DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                // Dùng SQLite shared memory, factory được share nhưng ta sẽ xoá/tạo lại DB mỗi test
                services.AddMemoryCache();
                services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseSqlite("DataSource=file:ControllerTestDb?mode=memory&cache=shared");
                });

                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });
            });
        });
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        db.Items.RemoveRange(db.Items);
        db.Connections.RemoveRange(db.Connections);
        db.Integrations.RemoveRange(db.Integrations);
        db.Users.RemoveRange(db.Users);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task TN2_FallbackTrigger_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync($"/api/connections/{Guid.NewGuid()}/sync", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SyncEndpoint_OwnershipAndExistenceBehaviors()
    {
        // Endpoint POST /api/connections/{id}/sync hiện chạy ĐỒNG BỘ qua IConnectionSyncDispatcher
        // (không còn background-job/jobId/429 — xem ConnectionSyncDispatcher.SyncAsync).
        // Test này khẳng định hợp đồng ownership/existence: 404 cho không-tồn-tại VÀ connection của user khác
        // (dispatcher cố ý KHÔNG lộ tồn tại resource của user khác → 404 thay vì 403).
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "TestToken_User1");

        var userId1 = Guid.Parse(TestAuthHandler.DefaultUserId);
        var user1 = new User { Id = userId1, Email = "u1@t.com", FullName = "U1" };
        var user2 = new User { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Email = "u2@t.com", FullName = "U2" };

        var integration = new Integration { Id = Guid.NewGuid(), Key = "g_" + Guid.NewGuid(), DisplayName = "G", IconUrl = "", Description = "", Provider = "Google", AuthorizationEndpoint = "", TokenEndpoint = "", SupportedServices = "[]" };

        var connUser2 = new Connection { Id = Guid.NewGuid(), UserId = user2.Id, Integration = integration, ProviderAccountId = "2", AccessTokenEncrypted = "a", RefreshTokenEncrypted = "r" };

        await ResetDatabaseAsync();

        using (var initScope = _factory.Services.CreateScope())
        {
            var initDb = initScope.ServiceProvider.GetRequiredService<AppDbContext>();
            initDb.Users.AddRange(user1, user2);
            initDb.Integrations.Add(integration);
            initDb.Connections.Add(connUser2);
            await initDb.SaveChangesAsync();
        }

        // 404: connection không tồn tại
        var response404 = await client.PostAsync($"/api/connections/{Guid.NewGuid()}/sync", null);
        var err404 = await response404.Content.ReadAsStringAsync();
        response404.StatusCode.Should().Be(HttpStatusCode.NotFound, $"Bởi vì connection không tồn tại trong DB. Error: {err404}");

        // 404: connection của user khác (dispatcher không lộ tồn tại → NotFound, không phải Forbidden)
        var responseOther = await client.PostAsync($"/api/connections/{connUser2.Id}/sync", null);
        responseOther.StatusCode.Should().Be(HttpStatusCode.NotFound, "Bởi vì connection thuộc user2 — dispatcher trả 404 để không lộ tồn tại resource của user khác");
    }

    [Fact]
    public async Task SyncEndpoint_UnsupportedServiceType_Returns422()
    {
        // ServiceType không hỗ trợ đồng bộ → dispatcher throw BusinessRuleException → 422.
        await ResetDatabaseAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "TestToken_User1");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userId1 = Guid.Parse(TestAuthHandler.DefaultUserId);
        var user1 = new User { Id = userId1, Email = "u1@t.com", FullName = "U1" };
        var integration = new Integration { Id = Guid.NewGuid(), Key = "g_" + Guid.NewGuid(), DisplayName = "G", IconUrl = "", Description = "", Provider = "Google", AuthorizationEndpoint = "", TokenEndpoint = "", SupportedServices = "[]" };

        var conn = new Connection
        {
            Id = Guid.NewGuid(),
            UserId = user1.Id,
            Integration = integration,
            ProviderAccountId = "1",
            AccessTokenEncrypted = "a",
            RefreshTokenEncrypted = "r",
            ServiceType = (ServiceType)999 // không hỗ trợ
        };

        if (!db.Users.Any(u => u.Id == userId1)) db.Users.Add(user1);
        db.Integrations.Add(integration);
        db.Connections.Add(conn);
        await db.SaveChangesAsync();

        var response = await client.PostAsync($"/api/connections/{conn.Id}/sync", null);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "Bởi vì ServiceType không hỗ trợ đồng bộ → BusinessRuleException → 422");
    }

    [Fact]
    public async Task SyncEndpoint_Fails_LastSyncedAtNotUpdated()
    {
        // Sync thất bại (ServiceType không hỗ trợ → 422) thì LastSyncedAt KHÔNG được cập nhật.
        // Endpoint chạy đồng bộ nên kiểm tra DB ngay sau response, không cần chờ background.
        await ResetDatabaseAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "TestToken_User1");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userId1 = Guid.Parse(TestAuthHandler.DefaultUserId);
        var user1 = new User { Id = userId1, Email = "u1@t.com", FullName = "U1" };
        var integration = new Integration { Id = Guid.NewGuid(), Key = "g_" + Guid.NewGuid(), DisplayName = "G", IconUrl = "", Description = "", Provider = "Google", AuthorizationEndpoint = "", TokenEndpoint = "", SupportedServices = "[]" };

        var initialLastSyncedAt = DateTime.UtcNow.AddDays(-1);
        var connUser1 = new Connection
        {
            Id = Guid.NewGuid(),
            UserId = user1.Id,
            Integration = integration,
            ProviderAccountId = "1",
            AccessTokenEncrypted = "a",
            RefreshTokenEncrypted = "r",
            LastSyncedAt = initialLastSyncedAt,
            ServiceType = (ServiceType)999 // không hỗ trợ → sync fail trước khi đụng provider
        };

        if (!db.Users.Any(u => u.Id == userId1))
        {
            db.Users.Add(user1);
        }
        db.Integrations.Add(integration);
        db.Connections.Add(connUser1);
        await db.SaveChangesAsync();

        var response = await client.PostAsync($"/api/connections/{connUser1.Id}/sync", null);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "Bởi vì ServiceType không hỗ trợ → 422");

        using var checkScope = _factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updatedConn = await checkDb.Connections.FindAsync(connUser1.Id);

        updatedConn.Should().NotBeNull();
        updatedConn!.LastSyncedAt.Should().Be(initialLastSyncedAt, "Bởi vì sync thất bại, LastSyncedAt không được cập nhật");
    }
}
