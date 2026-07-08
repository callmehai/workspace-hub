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

/// <summary>OData in-memory trên attribute route ([ODataIgnored]) — scheduled-emails, contacts, user-notifications.</summary>
public class ODataInMemoryEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private static readonly Guid UserId = Guid.Parse(TestAuthHandler.DefaultUserId);

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
    public async Task ContactsSuggest_Get_Filter_ReturnsAlice()
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
            $"/api/emails/contacts/suggest?connectionId={connId}&$filter={filter}&$count=true&$top=10");

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
