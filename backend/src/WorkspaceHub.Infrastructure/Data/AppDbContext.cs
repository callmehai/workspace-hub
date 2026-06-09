using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Infrastructure.Data.Converters;

namespace WorkspaceHub.Infrastructure.Data;

/// <summary>
/// EF Core DbContext — toàn bộ schema MVP (Sprint 1–3).
/// Quy ước: Guid PK, enum lưu string, datetime UTC (datetime2), JSON nvarchar(max).
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>Guid cố định cho seed integration Google (deterministic migration).</summary>
    public static readonly Guid GoogleIntegrationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Integration> Integrations => Set<Integration>();
    public DbSet<OAuthConnection> OAuthConnections => Set<OAuthConnection>();
    public DbSet<ServiceConnection> ServiceConnections => Set<ServiceConnection>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<FolderShare> FolderShares => Set<FolderShare>();
    public DbSet<ItemFolder> ItemFolders => Set<ItemFolder>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TagAssignment> TagAssignments => Set<TagAssignment>();
    public DbSet<ImportantContact> ImportantContacts => Set<ImportantContact>();
    public DbSet<ScheduledEmail> ScheduledEmails => Set<ScheduledEmail>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void ConfigureConventions(ModelConfigurationBuilder cfg)
    {
        // Enum → string (HasConversion<string>) cho toàn bộ enum domain.
        cfg.Properties<UserRole>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<ConnectionStatus>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<ServiceType>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<CursorType>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<ItemType>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<ItemStatus>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<SharePermission>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<ScheduledEmailStatus>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<ImportantContactType>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<NotificationType>().HaveConversion<string>().HaveMaxLength(30);

        // DateTime → luôn UTC.
        cfg.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        cfg.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // ---------- Nhóm 1: Auth ----------
        b.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        });

        // ---------- Nhóm 2: Integration & OAuth ----------
        b.Entity<Integration>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
        });

        b.Entity<OAuthConnection>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ProviderAccountId).HasMaxLength(256).IsRequired();
            e.HasIndex(x => new { x.UserId, x.IntegrationId, x.ProviderAccountId }).IsUnique();

            e.HasOne(x => x.User)
                .WithMany(u => u.OAuthConnections)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Xoá provider KHÔNG nuốt connection của user — Admin chỉ disable, không delete.
            e.HasOne(x => x.Integration)
                .WithMany(i => i.OAuthConnections)
                .HasForeignKey(x => x.IntegrationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ServiceConnection>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.OAuthConnection)
                .WithMany(o => o.ServiceConnections)
                .HasForeignKey(x => x.OAuthConnectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Nhóm 3: Core Workspace ----------
        b.Entity<Item>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ExternalId).HasMaxLength(512);
            e.Property(x => x.MetadataJson).IsRequired(); // nvarchar(max) (không set length)

            // Chống duplicate khi re-sync. Lọc NULL vì Note không có ExternalId.
            e.HasIndex(x => new { x.ServiceConnectionId, x.ExternalId })
                .IsUnique()
                .HasFilter("[ExternalId] IS NOT NULL");

            // Query Kanban/Inbox nhanh.
            e.HasIndex(x => new { x.UserId, x.Status, x.OccurredAt })
                .HasDatabaseName("IX_Items_User_Status_OccurredAt");

            e.HasOne(x => x.User)
                .WithMany(u => u.Items)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Xoá connection vẫn giữ Item history → ServiceConnectionId = NULL.
            // Để NoAction ở DB (tránh multiple-cascade-path của SQL Server); service disconnect
            // set null các Item trước khi xoá ServiceConnection (xem SCRUM-14 / API 4.5).
            e.HasOne(x => x.ServiceConnection)
                .WithMany(s => s.Items)
                .HasForeignKey(x => x.ServiceConnectionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<Folder>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.Owner)
                .WithMany(u => u.Folders)
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FolderShare>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.FolderId, x.SharedWithUserId }).IsUnique();

            e.HasOne(x => x.Folder)
                .WithMany(f => f.FolderShares)
                .HasForeignKey(x => x.FolderId)
                .OnDelete(DeleteBehavior.Cascade);

            // Hai FK trỏ về Users → NoAction để tránh multiple cascade path.
            e.HasOne(x => x.SharedWithUser)
                .WithMany()
                .HasForeignKey(x => x.SharedWithUserId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<ItemFolder>(e =>
        {
            e.HasKey(x => new { x.ItemId, x.FolderId }); // composite PK

            e.HasOne(x => x.Folder)
                .WithMany(f => f.ItemFolders)
                .HasForeignKey(x => x.FolderId)
                .OnDelete(DeleteBehavior.Cascade);

            // Item → ItemFolder NoAction (tránh 2 đường cascade từ User: qua Item và qua Folder).
            e.HasOne(x => x.Item)
                .WithMany(i => i.ItemFolders)
                .HasForeignKey(x => x.ItemId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ---------- Nhóm 4: Features ----------
        b.Entity<Tag>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasOne(x => x.User)
                .WithMany(u => u.Tags)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TagAssignment>(e =>
        {
            e.HasKey(x => new { x.TagId, x.ItemId }); // composite PK

            e.HasOne(x => x.Tag)
                .WithMany(t => t.TagAssignments)
                .HasForeignKey(x => x.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            // Item → TagAssignment NoAction (tránh 2 đường cascade từ User: qua Item và qua Tag).
            e.HasOne(x => x.Item)
                .WithMany(i => i.TagAssignments)
                .HasForeignKey(x => x.ItemId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<ImportantContact>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Identifier).HasMaxLength(256).IsRequired();
            e.HasIndex(x => new { x.UserId, x.Type, x.Identifier }).IsUnique();
            e.HasOne(x => x.User)
                .WithMany(u => u.ImportantContacts)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ScheduledEmail>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Subject).HasMaxLength(500).IsRequired();
            e.HasIndex(x => new { x.Status, x.SendAt })
                .HasFilter("[Status] = 'Pending'")
                .HasDatabaseName("IX_ScheduledEmails_Pending_SendAt");

            e.HasOne(x => x.User)
                .WithMany(u => u.ScheduledEmails)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Xoá connection có scheduled email Pending → NoAction (app xử lý trước).
            e.HasOne(x => x.ServiceConnection)
                .WithMany(s => s.ScheduledEmails)
                .HasForeignKey(x => x.ServiceConnectionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<Notification>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt })
                .HasDatabaseName("IX_Notifications_User_IsRead_CreatedAt");
            e.HasOne(x => x.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Seed: 1 Integration Google ----------
        b.Entity<Integration>().HasData(new Integration
        {
            Id = GoogleIntegrationId,
            Key = "google",
            DisplayName = "Google Workspace",
            IconUrl = "https://www.google.com/favicon.ico",
            Description = "Gmail · Calendar · Drive",
            Provider = "Google",
            ClientIdEncrypted = "",     // Admin nhập + encrypt ở SCRUM-13
            ClientSecretEncrypted = "",
            AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
            TokenEndpoint = "https://oauth2.googleapis.com/token",
            DefaultScopes = "gmail.readonly calendar.readonly drive.readonly",
            SupportedServices = "[\"Gmail\",\"GCal\",\"Drive\"]",
            IsEnabled = true
        });
    }
}
