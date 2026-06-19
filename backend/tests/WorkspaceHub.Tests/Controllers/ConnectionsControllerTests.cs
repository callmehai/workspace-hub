using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
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
    public async Task TN2_FallbackTrigger_EndpointBehaviors()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "TestToken_User1");

        var userId1 = Guid.Parse(TestAuthHandler.DefaultUserId);
        var user1 = new User { Id = userId1, Email = "u1@t.com", FullName = "U1" };
        var user2 = new User { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Email = "u2@t.com", FullName = "U2" };
        
        var integration = new Integration { Id = Guid.NewGuid(), Key = "g_" + Guid.NewGuid(), DisplayName = "G", IconUrl = "", Description = "", Provider = "Google", AuthorizationEndpoint = "", TokenEndpoint = "", SupportedServices = "[]" };
        
        var connUser1 = new Connection { Id = Guid.NewGuid(), UserId = user1.Id, Integration = integration, ProviderAccountId = "1", AccessTokenEncrypted = "a", RefreshTokenEncrypted = "r" };
        var connUser2 = new Connection { Id = Guid.NewGuid(), UserId = user2.Id, Integration = integration, ProviderAccountId = "2", AccessTokenEncrypted = "a", RefreshTokenEncrypted = "r" };
        
        await ResetDatabaseAsync();

        using (var initScope = _factory.Services.CreateScope())
        {
            var initDb = initScope.ServiceProvider.GetRequiredService<AppDbContext>();
            initDb.Users.AddRange(user1, user2);
            initDb.Integrations.Add(integration);
            initDb.Connections.AddRange(connUser1, connUser2);
            await initDb.SaveChangesAsync();
        }

        // Test 404: Không tồn tại
        var response404 = await client.PostAsync($"/api/connections/{Guid.NewGuid()}/sync", null);
        var err404 = await response404.Content.ReadAsStringAsync();
        response404.StatusCode.Should().Be(HttpStatusCode.NotFound, $"Bởi vì connection không tồn tại trong DB. Error: {err404}");

        // Test 403: Của user khác
        var response403 = await client.PostAsync($"/api/connections/{connUser2.Id}/sync", null);
        response403.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Bởi vì connection thuộc về user2, không phải user đang gọi");

        // Test 202 + Body có JobId
        var response202 = await client.PostAsync($"/api/connections/{connUser1.Id}/sync", null);
        response202.StatusCode.Should().Be(HttpStatusCode.Accepted, "Bởi vì connection hợp lệ");
        
        var content = await response202.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(content);
        doc.TryGetProperty("jobId", out _).Should().BeTrue("Bởi vì body phải trả về một jobId để track");

        // Test 429: Gọi lại quá gần
        var response429 = await client.PostAsync($"/api/connections/{connUser1.Id}/sync", null);
        response429.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "Bởi vì vừa mới gọi sync xong, cần rate limit chống spam");
    }

    [Fact]
    public async Task TN2_FallbackTrigger_NotThrottledByBackgroundSync()
    {
        await ResetDatabaseAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "TestToken_User1");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var userId1 = Guid.Parse(TestAuthHandler.DefaultUserId);
        var user1 = new User { Id = userId1, Email = "u1@t.com", FullName = "U1" };
        var integration = new Integration { Id = Guid.NewGuid(), Key = "g_" + Guid.NewGuid(), DisplayName = "G", IconUrl = "", Description = "", Provider = "Google", AuthorizationEndpoint = "", TokenEndpoint = "", SupportedServices = "[]" };
        
        // Cố tình set LastSyncedAt là vừa mới đây (background vừa chạy xong)
        var connUser1 = new Connection { 
            Id = Guid.NewGuid(), 
            UserId = user1.Id, 
            Integration = integration, 
            ProviderAccountId = "1", 
            AccessTokenEncrypted = "a", 
            RefreshTokenEncrypted = "r",
            LastSyncedAt = DateTime.UtcNow // Ngay bây giờ!
        };

        if (!db.Users.Any(u => u.Id == userId1))
        {
            db.Users.Add(user1);
        }
        db.Integrations.Add(integration);
        db.Connections.Add(connUser1);
        await db.SaveChangesAsync();

        // Mặc dù LastSyncedAt mới, nhưng chưa hề gọi bằng tay, nên cache trống, KHÔNG được 429
        var response202 = await client.PostAsync($"/api/connections/{connUser1.Id}/sync", null);
        response202.StatusCode.Should().Be(HttpStatusCode.Accepted, "Không được rate limit dựa trên LastSyncedAt, LastSyncedAt chỉ lưu kết quả của Background Sync");
        
        // Gọi lại thì mới dính 429 vì cache đã lưu
        var response429 = await client.PostAsync($"/api/connections/{connUser1.Id}/sync", null);
        response429.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task TN2_FallbackTrigger_JobFails_LastSyncedAtNotUpdated()
    {
        await ResetDatabaseAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "TestToken_User1");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var userId1 = Guid.Parse(TestAuthHandler.DefaultUserId);
        var user1 = new User { Id = userId1, Email = "u1@t.com", FullName = "U1" };
        var integration = new Integration { Id = Guid.NewGuid(), Key = "g_" + Guid.NewGuid(), DisplayName = "G", IconUrl = "", Description = "", Provider = "Google", AuthorizationEndpoint = "", TokenEndpoint = "", SupportedServices = "[]" };
        
        var initialLastSyncedAt = DateTime.UtcNow.AddDays(-1);
        var connUser1 = new Connection { 
            Id = Guid.NewGuid(), 
            UserId = user1.Id, 
            Integration = integration, 
            ProviderAccountId = "1", 
            AccessTokenEncrypted = "a", 
            RefreshTokenEncrypted = "r",
            LastSyncedAt = initialLastSyncedAt,
            ServiceType = (ServiceType)999 // Not supported service type, will cause failure or early exit
        };

        if (!db.Users.Any(u => u.Id == userId1))
        {
            db.Users.Add(user1);
        }
        db.Integrations.Add(integration);
        db.Connections.Add(connUser1);
        await db.SaveChangesAsync();

        var response202 = await client.PostAsync($"/api/connections/{connUser1.Id}/sync", null);
        response202.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Wait for background job to run
        await Task.Delay(500);

        // Check DB
        using var checkScope = _factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updatedConn = await checkDb.Connections.FindAsync(connUser1.Id);

        updatedConn.Should().NotBeNull();
        updatedConn!.LastSyncedAt.Should().Be(initialLastSyncedAt, "Bởi vì sync thất bại/bị skip, LastSyncedAt không được cập nhật");
    }
}
