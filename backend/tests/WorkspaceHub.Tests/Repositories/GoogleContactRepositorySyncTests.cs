using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Contacts;
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

    [Fact]
    public async Task SyncForConnectionAsync_PreservesContactWithoutEmail()
    {
        var (db, connectionId) = await CreateSeededDbAsync();
        await using (db)
        {
            var repo = new GoogleContactRepository(db);
            var contactId = Guid.NewGuid();
            var profile = new ContactProfileDto { GivenName = "Phone", FamilyName = "Only", Phones = [new LabeledPhoneDto { Value = "0901234567" }] };
            var metadata = ContactProfileJson.Serialize(profile);

            await repo.SyncForConnectionAsync(connectionId, new[]
            {
                new GoogleContact
                {
                    Id = contactId,
                    ConnectionId = connectionId,
                    Email = null,
                    DisplayName = "Phone Only",
                    Source = GoogleContactSource.Contact,
                    ExternalResourceName = "people/c-no-email",
                    MetadataJson = metadata,
                    SyncedAt = DateTime.UtcNow,
                }
            });

            var row = await db.GoogleContacts.SingleAsync(c => c.ConnectionId == connectionId);
            row.Id.Should().Be(contactId);
            row.Email.Should().BeNull();
            row.ExternalResourceName.Should().Be("people/c-no-email");
        }
    }

    [Fact]
    public async Task ApplyDetailToResourceAsync_UpsertsSingleRowPerResource()
    {
        var (db, connectionId) = await CreateSeededDbAsync();
        await using (db)
        {
            var repo = new GoogleContactRepository(db);
            var contactRowId = Guid.NewGuid();
            var resourceName = "people/c256";
            var syncedAt = DateTime.UtcNow.AddDays(-1);

            db.GoogleContacts.Add(new GoogleContact
            {
                Id = contactRowId,
                ConnectionId = connectionId,
                Email = "work@example.com",
                DisplayName = "Work",
                Source = GoogleContactSource.Contact,
                ExternalResourceName = resourceName,
                Etag = "etag-old",
                SyncedAt = syncedAt,
            });
            await db.SaveChangesAsync();

            var profile = ContactProfileJson.BuildSimple("phamgiakhanh0709@gmail.com", "Khanh");
            profile.Emails.Add(new LabeledEmailDto { Value = "work@example.com", Label = "work" });
            var detail = new PeopleContactDetail
            {
                Email = "phamgiakhanh0709@gmail.com",
                DisplayName = "Khanh",
                Etag = "etag-new",
                ResourceName = resourceName,
                Profile = profile,
            };

            var updatedAt = DateTime.UtcNow;
            await repo.ApplyDetailToResourceAsync(connectionId, resourceName, detail, updatedAt);

            var rows = await db.GoogleContacts.Where(c => c.ConnectionId == connectionId).ToListAsync();
            rows.Should().HaveCount(1);
            rows[0].Id.Should().Be(contactRowId);
            rows[0].Email.Should().Be("work@example.com");
            rows[0].Etag.Should().Be("etag-new");
            rows[0].MetadataJson.Should().Contain("work@example.com");
        }
    }
}
