using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Tests.Controllers;

/// <summary>OData in-memory trên convention route — ScheduledEmails, EmailContactSuggestions, Notifications.</summary>
public class ODataInMemoryEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private static readonly Guid UserId = Guid.Parse(TestAuthHandler.DefaultUserId);
    private static readonly Guid User2Id = Guid.Parse(TestAuthHandler.User2Id);

    public ODataInMemoryEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var descriptor = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddMemoryCache();
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite("DataSource=file:ODataTestDb?mode=memory&cache=shared"));

                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            });
        });
    }

    private HttpClient CreateAuthClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "TestToken_User1");
        return client;
    }

    private static HttpClient CreateAuthClientForUser2(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "TestToken_User2");
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        db.Notifications.RemoveRange(db.Notifications);
        db.ScheduledEmails.RemoveRange(db.ScheduledEmails);
        db.GoogleContacts.RemoveRange(db.GoogleContacts);
        db.Connections.RemoveRange(db.Connections);
        db.Integrations.RemoveRange(db.Integrations);
        db.Users.RemoveRange(db.Users);
        await db.SaveChangesAsync();

        var user = new User { Id = UserId, Email = "u1@test.com", FullName = "U1", Role = UserRole.User };
        var integration = new Integration
        {
            Id = Guid.NewGuid(),
            Key = "google_" + Guid.NewGuid(),
            DisplayName = "Google",
            IconUrl = "",
            Description = "",
            Provider = "Google",
            AuthorizationEndpoint = "https://example.com/auth",
            TokenEndpoint = "https://example.com/token",
            SupportedServices = "[]",
            IsEnabled = true,
        };
        var conn = new Connection
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            IntegrationId = integration.Id,
            Integration = integration,
            ProviderAccountId = "g1",
            AccessTokenEncrypted = "a",
            RefreshTokenEncrypted = "r",
            ServiceType = ServiceType.Gmail,
            Status = ConnectionStatus.Active,
        };

        db.Users.Add(user);
        db.Integrations.Add(integration);
        db.Connections.Add(conn);

        db.ScheduledEmails.AddRange(
            new ScheduledEmail
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                ConnectionId = conn.Id,
                Connection = conn,
                Subject = "Pending mail",
                BodyHtml = "<p>hi</p>",
                ToJson = "[\"a@test.com\"]",
                SendAt = DateTime.UtcNow.AddHours(1),
                Status = ScheduledEmailStatus.Pending,
                CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            },
            new ScheduledEmail
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                ConnectionId = conn.Id,
                Connection = conn,
                Subject = "Sent mail",
                BodyHtml = "<p>done</p>",
                ToJson = "[\"b@test.com\"]",
                SendAt = DateTime.UtcNow.AddHours(-1),
                Status = ScheduledEmailStatus.Sent,
                CreatedAt = DateTime.UtcNow,
            });

        db.GoogleContacts.AddRange(
            new GoogleContact
            {
                Id = Guid.NewGuid(),
                ConnectionId = conn.Id,
                Connection = conn,
                Email = "alice@example.com",
                DisplayName = "Alice",
                Source = GoogleContactSource.Contact,
                SyncedAt = DateTime.UtcNow,
            },
            new GoogleContact
            {
                Id = Guid.NewGuid(),
                ConnectionId = conn.Id,
                Connection = conn,
                Email = "bob@example.com",
                DisplayName = "Bob",
                Source = GoogleContactSource.OtherContact,
                SyncedAt = DateTime.UtcNow,
            });

        db.Notifications.AddRange(
            new Notification
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                Type = NotificationType.ItemSynced,
                Title = "Unread",
                Body = "body",
                LinkUrl = "/inbox",
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
            },
            new Notification
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                Type = NotificationType.ItemSynced,
                Title = "Read",
                Body = "body",
                LinkUrl = "/inbox",
                IsRead = true,
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            });

        await db.SaveChangesAsync();
    }

    private async Task SeedUserIsolationAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        db.Notifications.RemoveRange(db.Notifications);
        db.ScheduledEmails.RemoveRange(db.ScheduledEmails);
        db.Connections.RemoveRange(db.Connections);
        db.Integrations.RemoveRange(db.Integrations);
        db.Users.RemoveRange(db.Users);
        await db.SaveChangesAsync();

        var integration = new Integration
        {
            Id = Guid.NewGuid(),
            Key = "google_iso",
            DisplayName = "Google",
            IconUrl = "",
            Description = "",
            Provider = "Google",
            AuthorizationEndpoint = "https://example.com/auth",
            TokenEndpoint = "https://example.com/token",
            SupportedServices = "[]",
            IsEnabled = true,
        };

        var user1 = new User { Id = UserId, Email = "u1@test.com", FullName = "U1", Role = UserRole.User };
        var user2 = new User { Id = User2Id, Email = "u2@test.com", FullName = "U2", Role = UserRole.User };

        var conn1 = new Connection
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            IntegrationId = integration.Id,
            Integration = integration,
            ProviderAccountId = "g1",
            AccessTokenEncrypted = "a",
            RefreshTokenEncrypted = "r",
            ServiceType = ServiceType.Gmail,
            Status = ConnectionStatus.Active,
        };

        var conn2 = new Connection
        {
            Id = Guid.NewGuid(),
            UserId = User2Id,
            IntegrationId = integration.Id,
            ProviderAccountId = "g2",
            AccessTokenEncrypted = "a",
            RefreshTokenEncrypted = "r",
            ServiceType = ServiceType.Gmail,
            Status = ConnectionStatus.Active,
        };

        db.Users.AddRange(user1, user2);
        db.Integrations.Add(integration);
        db.Connections.AddRange(conn1, conn2);

        db.Notifications.AddRange(
            new Notification
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                Type = NotificationType.ItemSynced,
                Title = "User1 only",
                Body = "body",
                LinkUrl = "/inbox",
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
            },
            new Notification
            {
                Id = Guid.NewGuid(),
                UserId = User2Id,
                Type = NotificationType.ItemSynced,
                Title = "User2 only",
                Body = "body",
                LinkUrl = "/inbox",
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
            });

        db.ScheduledEmails.AddRange(
            new ScheduledEmail
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                ConnectionId = conn1.Id,
                Connection = conn1,
                Subject = "User1 scheduled",
                BodyHtml = "<p>u1</p>",
                ToJson = "[\"u1@test.com\"]",
                SendAt = DateTime.UtcNow.AddHours(1),
                Status = ScheduledEmailStatus.Pending,
                CreatedAt = DateTime.UtcNow,
            },
            new ScheduledEmail
            {
                Id = Guid.NewGuid(),
                UserId = User2Id,
                ConnectionId = conn2.Id,
                Connection = conn2,
                Subject = "User2 scheduled",
                BodyHtml = "<p>u2</p>",
                ToJson = "[\"u2@test.com\"]",
                SendAt = DateTime.UtcNow.AddHours(1),
                Status = ScheduledEmailStatus.Pending,
                CreatedAt = DateTime.UtcNow,
            });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Notifications_Get_UserIsolation_OnlyReturnsOwnRows()
    {
        await SeedUserIsolationAsync();
        var client1 = CreateAuthClient();
        var client2 = CreateAuthClientForUser2(_factory);

        var response1 = await client1.GetAsync("/api/Notifications?$count=true&$top=10");
        var response2 = await client2.GetAsync("/api/Notifications?$count=true&$top=10");

        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        var (items1, total1) = await ParseODataCollectionAsync(response1);
        var (items2, total2) = await ParseODataCollectionAsync(response2);

        total1.Should().Be(1);
        total2.Should().Be(1);
        items1[0].GetProperty("title").GetString().Should().Be("User1 only");
        items2[0].GetProperty("title").GetString().Should().Be("User2 only");
    }

    [Fact]
    public async Task ScheduledEmails_Get_UserIsolation_OnlyReturnsOwnRows()
    {
        await SeedUserIsolationAsync();
        var client1 = CreateAuthClient();
        var client2 = CreateAuthClientForUser2(_factory);

        var response1 = await client1.GetAsync("/api/ScheduledEmails?$count=true&$top=10");
        var response2 = await client2.GetAsync("/api/ScheduledEmails?$count=true&$top=10");

        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        var (items1, total1) = await ParseODataCollectionAsync(response1);
        var (items2, total2) = await ParseODataCollectionAsync(response2);

        total1.Should().Be(1);
        total2.Should().Be(1);
        items1[0].GetProperty("subject").GetString().Should().Be("User1 scheduled");
        items2[0].GetProperty("subject").GetString().Should().Be("User2 scheduled");
    }

    [Fact]
    public async Task ScheduledEmails_Get_ReturnsItems_WithCount()
    {
        await ResetAndSeedAsync();
        var client = CreateAuthClient();

        var response = await client.GetAsync("/api/ScheduledEmails?$count=true&$top=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var (items, total) = await ParseODataCollectionAsync(response);
        items.Should().HaveCount(2);
        total.Should().Be(2);
    }

    [Fact]
    public async Task ScheduledEmails_Get_StatusFilter_ReturnsPendingOnly()
    {
        await ResetAndSeedAsync();
        var client = CreateAuthClient();

        var response = await client.GetAsync(
            "/api/ScheduledEmails?$filter=Status eq 'Pending'&$count=true&$top=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var (items, total) = await ParseODataCollectionAsync(response);
        items.Should().HaveCount(1);
        total.Should().Be(1);
        items[0].GetProperty("status").GetString().Should().Be("Pending");
    }

    [Fact]
    public async Task Notifications_Get_UnreadFilter_ReturnsOne()
    {
        await ResetAndSeedAsync();
        var client = CreateAuthClient();

        var response = await client.GetAsync(
            "/api/Notifications?$filter=IsRead eq false&$count=true&$top=10&$orderby=CreatedAt desc");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var (items, total) = await ParseODataCollectionAsync(response);
        items.Should().HaveCount(1);
        total.Should().Be(1);
        items[0].GetProperty("isRead").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task EmailContactSuggestions_Get_Filter_ReturnsAlice()
    {
        await ResetAndSeedAsync();
        using var scope = _factory.Services.CreateScope();
        var connId = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Connections.Where(c => c.UserId == UserId)
            .Select(c => c.Id)
            .FirstAsync();

        var client = CreateAuthClient();
        var filter = Uri.EscapeDataString("contains(Email,'alice')");
        var response = await client.GetAsync(
            $"/api/EmailContactSuggestions?connectionId={connId}&$filter={filter}&$count=true&$top=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var (items, _) = await ParseODataCollectionAsync(response);
        items.Should().HaveCount(1);
        items[0].GetProperty("email").GetString().Should().Be("alice@example.com");
    }

    private static async Task<(JsonElement[] Items, int Total)> ParseODataCollectionAsync(HttpResponseMessage response)
    {
        var json = await ParseJsonAsync(response);
        if (json.ValueKind == JsonValueKind.Array)
        {
            var arr = json.EnumerateArray().ToArray();
            return (arr, arr.Length);
        }

        json.TryGetProperty("value", out var value).Should().BeTrue("OData phải trả object có value, không phải array thuần");
        json.TryGetProperty("@odata.count", out _).Should().BeTrue("OData $count=true phải có @odata.count");
        var items = value.EnumerateArray().ToArray();
        var total = json.TryGetProperty("@odata.count", out var count) ? count.GetInt32() : items.Length;
        return (items, total);
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }
}
