using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Infrastructure.Data;
using WorkspaceHub.Infrastructure.Repositories;

namespace WorkspaceHub.Tests.Repositories;

public class GoogleContactRepositorySyncTests
{
    private static async Task<(AppDbContext Db, Guid ConnectionId)> CreateSeededDbAsync()
    {
        var dbName = $"GoogleContactSyncTest_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"DataSource=file:{dbName}?mode=memory&cache=shared")
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();

        var userId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();

        db.Users.Add(new User { Id = userId, Email = "u@test.com", FullName = "U", Role = UserRole.User });
        db.Integrations.Add(new Integration
        {
            Id = integrationId,
            Key = "google_" + Guid.NewGuid(),
            DisplayName = "Google",
            IconUrl = "",
            Description = "",
            Provider = "Google",
            AuthorizationEndpoint = "https://example.com/auth",
            TokenEndpoint = "https://example.com/token",
            SupportedServices = "[]",
            IsEnabled = true,
        });
        db.Connections.Add(new Connection
        {
            Id = connectionId,
            UserId = userId,
            IntegrationId = integrationId,
            ProviderAccountId = "g1",
            AccessTokenEncrypted = "a",
            RefreshTokenEncrypted = "r",
            ServiceType = ServiceType.Gmail,
            Status = ConnectionStatus.Active,
        });
        await db.SaveChangesAsync();
        return (db, connectionId);
    }

    [Fact]
    public async Task SyncForConnectionAsync_PreservesIdAndUpdatedAt_OnSecondSync()
    {
        var (db, connectionId) = await CreateSeededDbAsync();
        await using (db)
        {
            var repo = new GoogleContactRepository(db);
            var writeBackAt = DateTime.UtcNow.AddHours(-1);

            await repo.SyncForConnectionAsync(connectionId, new[]
            {
                new GoogleContact
                {
                    Id = Guid.NewGuid(),
                    ConnectionId = connectionId,
                    Email = "alice@example.com",
                    DisplayName = "Alice",
                    Source = GoogleContactSource.Contact,
                    ExternalResourceName = "people/c1",
                    Etag = "etag-1",
                    SyncedAt = DateTime.UtcNow.AddDays(-1)
                }
            });

            var preservedId = (await db.GoogleContacts.SingleAsync(c => c.ConnectionId == connectionId)).Id;
            var tracked = await db.GoogleContacts.SingleAsync(c => c.ConnectionId == connectionId);
            tracked.UpdatedAt = writeBackAt;
            await db.SaveChangesAsync();

            await repo.SyncForConnectionAsync(connectionId, new[]
            {
                new GoogleContact
                {
                    Id = Guid.NewGuid(),
                    ConnectionId = connectionId,
                    Email = "alice@example.com",
                    DisplayName = "Alice Updated",
                    Source = GoogleContactSource.Contact,
                    ExternalResourceName = "people/c1",
                    Etag = "etag-2",
                    SyncedAt = DateTime.UtcNow
                }
            });

            var afterSecond = await db.GoogleContacts.SingleAsync(c => c.ConnectionId == connectionId);
            afterSecond.Id.Should().Be(preservedId);
            afterSecond.UpdatedAt.Should().Be(writeBackAt);
            afterSecond.DisplayName.Should().Be("Alice Updated");
            afterSecond.Etag.Should().Be("etag-2");
        }
    }

    [Fact]
    public async Task SyncForConnectionAsync_RemovesOrphans()
    {
        var (db, connectionId) = await CreateSeededDbAsync();
        await using (db)
        {
            var repo = new GoogleContactRepository(db);

            await repo.SyncForConnectionAsync(connectionId, new[]
            {
                new GoogleContact
                {
                    Id = Guid.NewGuid(),
                    ConnectionId = connectionId,
                    Email = "alice@example.com",
                    DisplayName = "Alice",
                    Source = GoogleContactSource.Contact,
                    ExternalResourceName = "people/c1",
                    SyncedAt = DateTime.UtcNow
                },
                new GoogleContact
                {
                    Id = Guid.NewGuid(),
                    ConnectionId = connectionId,
                    Email = "bob@example.com",
                    DisplayName = "Bob",
                    Source = GoogleContactSource.OtherContact,
                    ExternalResourceName = "people/c2",
                    SyncedAt = DateTime.UtcNow
                }
            });

            await repo.SyncForConnectionAsync(connectionId, new[]
            {
                new GoogleContact
                {
                    Id = Guid.NewGuid(),
                    ConnectionId = connectionId,
                    Email = "alice@example.com",
                    DisplayName = "Alice",
                    Source = GoogleContactSource.Contact,
                    ExternalResourceName = "people/c1",
                    SyncedAt = DateTime.UtcNow
                }
            });

            var remaining = await db.GoogleContacts.Where(c => c.ConnectionId == connectionId).ToListAsync();
            remaining.Should().HaveCount(1);
            remaining[0].Email.Should().Be("alice@example.com");
        }
    }
}
