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
    /// <summary>Guid cố định cho seed integration Atlassian/Jira (deterministic migration — SCRUM-54).</summary>
    public static readonly Guid AtlassianIntegrationId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Integration> Integrations => Set<Integration>();
    public DbSet<Connection> Connections => Set<Connection>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<FolderShare> FolderShares => Set<FolderShare>();
    public DbSet<ItemFolder> ItemFolders => Set<ItemFolder>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TagAssignment> TagAssignments => Set<TagAssignment>();
    public DbSet<ImportantContact> ImportantContacts => Set<ImportantContact>();
    public DbSet<GoogleContact> GoogleContacts => Set<GoogleContact>();
    public DbSet<ScheduledEmail> ScheduledEmails => Set<ScheduledEmail>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<EventReminder> EventReminders => Set<EventReminder>();
    public DbSet<CalendarInvitation> CalendarInvitations => Set<CalendarInvitation>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<FriendInvite> FriendInvites => Set<FriendInvite>();

    protected override void ConfigureConventions(ModelConfigurationBuilder cfg)
    {
        // Enum → string (HasConversion<string>) cho toàn bộ enum domain.
        cfg.Properties<UserRole>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<ProviderType>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<ConnectionStatus>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<ServiceType>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<CursorType>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<ItemType>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<ItemStatus>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<SharePermission>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<ScheduledEmailStatus>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<ImportantContactType>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<GoogleContactSource>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<NotificationType>().HaveConversion<string>().HaveMaxLength(30);
        cfg.Properties<AuthProvider>().HaveConversion<string>().HaveMaxLength(10);
        cfg.Properties<ReminderUnit>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<CalendarInvitationStatus>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<FriendshipStatus>().HaveConversion<string>().HaveMaxLength(20);
        cfg.Properties<FriendTier>().HaveConversion<string>().HaveMaxLength(20);

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
            e.Property(x => x.Phone).HasMaxLength(20);          // E.164, tối đa 15 chữ số + dấu +
            e.Property(x => x.PhoneVerified).HasDefaultValue(true); // user cũ + Google không bị chặn
            e.Property(x => x.GoogleSub).HasMaxLength(256);
            e.HasIndex(x => x.GoogleSub)
                .IsUnique()
                .HasFilter("[GoogleSub] IS NOT NULL")
                .HasDatabaseName("IX_Users_GoogleSub");
        });

        // ---------- Nhóm 2: Integration & OAuth ----------
        b.Entity<Integration>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
        });

        // Mô hình B: mỗi service = 1 row Connections, token riêng.
        b.Entity<Connection>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ProviderAccountId).HasMaxLength(256).IsRequired();
            e.HasIndex(x => new { x.UserId, x.Provider, x.ServiceType, x.ProviderAccountId }).IsUnique();

            e.HasOne(x => x.User)
                .WithMany(u => u.Connections)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Xoá provider KHÔNG nuốt connection của user — Admin chỉ disable, không delete.
            e.HasOne(x => x.Integration)
                .WithMany(i => i.Connections)
                .HasForeignKey(x => x.IntegrationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---------- Nhóm 3: Core Workspace ----------
        b.Entity<Item>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ExternalId).HasMaxLength(512);
            e.Property(x => x.ThreadId).HasMaxLength(512); // Gmail threadId — gộp thread ở list
            e.Property(x => x.ETag).HasMaxLength(512);    // version provider cho write-back conflict
            e.Property(x => x.MetadataJson).IsRequired(); // nvarchar(max) (không set length)

            // Gộp thread ở list: lọc theo user + thread, chọn message mới nhất.
            e.HasIndex(x => new { x.UserId, x.ThreadId })
                .HasDatabaseName("IX_Items_User_ThreadId")
                .HasFilter("[ThreadId] IS NOT NULL");

            // Chống duplicate khi re-sync. Lọc NULL vì Note không có ExternalId.
            e.HasIndex(x => new { x.ConnectionId, x.ExternalId })
                .IsUnique()
                .HasFilter("[ExternalId] IS NOT NULL");

            // Query Kanban/Inbox nhanh.
            e.HasIndex(x => new { x.UserId, x.Status, x.OccurredAt })
                .HasDatabaseName("IX_Items_User_Status_OccurredAt");

            e.HasOne(x => x.User)
                .WithMany(u => u.Items)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Xoá connection vẫn giữ Item history → ConnectionId = NULL.
            // Để NoAction ở DB (tránh multiple-cascade-path của SQL Server); service disconnect
            // set null các Item trước khi xoá Connection (xem SCRUM-14 / API 4.5).
            e.HasOne(x => x.Connection)
                .WithMany(c => c.Items)
                .HasForeignKey(x => x.ConnectionId)
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

        // ---------- Bạn bè nội bộ app ----------
        b.Entity<Friendship>(e =>
        {
            e.HasKey(x => x.Id);
            // 1 row / cặp; chiều ngược kiểm tra ở service (query cả 2 chiều trước khi tạo).
            e.HasIndex(x => new { x.RequesterId, x.AddresseeId }).IsUnique();
            e.HasIndex(x => x.AddresseeId);

            // Hai FK trỏ về Users → NoAction để tránh multiple cascade path (như FolderShare).
            e.HasOne(x => x.Requester)
                .WithMany()
                .HasForeignKey(x => x.RequesterId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasOne(x => x.Addressee)
                .WithMany()
                .HasForeignKey(x => x.AddresseeId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<FriendInvite>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.Token).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.Email);                                          // consume theo email khi user mới đăng ký
            e.HasIndex(x => new { x.InviterUserId, x.Email }).IsUnique();      // 1 invite sống / (inviter, email)

            e.HasOne(x => x.Inviter)
                .WithMany()
                .HasForeignKey(x => x.InviterUserId)
                .OnDelete(DeleteBehavior.Cascade);
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

            // Tên tag unique trong 1 user (SCRUM-70 fix TOCTOU). Collation cột mặc định của
            // SQL Server là case-insensitive → khớp với check case-insensitive ở service layer.
            e.HasIndex(x => new { x.UserId, x.Name }).IsUnique();
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

        b.Entity<GoogleContact>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(320).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.ExternalResourceName).HasMaxLength(256);
            e.HasIndex(x => new { x.ConnectionId, x.Email }).IsUnique();
            e.HasIndex(x => new { x.ConnectionId, x.DisplayName })
                .HasDatabaseName("IX_GoogleContacts_ConnectionId_DisplayName");

            e.HasOne(x => x.Connection)
                .WithMany(c => c.GoogleContacts)
                .HasForeignKey(x => x.ConnectionId)
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
            e.HasOne(x => x.Connection)
                .WithMany(c => c.ScheduledEmails)
                .HasForeignKey(x => x.ConnectionId)
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

        b.Entity<EventReminder>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ReminderType).HasMaxLength(20).HasConversion<string>();
            e.Property(x => x.TimeOfDay).HasMaxLength(10);
            e.HasOne(x => x.EventItem)
                .WithMany(i => i.Reminders)
                .HasForeignKey(x => x.EventItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CalendarInvitation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.InviteeEmail).HasMaxLength(320).IsRequired();
            e.Property(x => x.GoogleEventId).HasMaxLength(512).IsRequired();
            e.Property(x => x.ICalUid).HasMaxLength(512);
            e.HasIndex(x => new { x.OrganizerItemId, x.InviteeEmail }).IsUnique();
            e.HasIndex(x => new { x.InviteeUserId, x.Status, x.UpdatedAt });
            e.HasIndex(x => new { x.ICalUid, x.InviteeEmail })
                .HasFilter("[ICalUid] IS NOT NULL");

            e.HasOne(x => x.OrganizerItem)
                .WithMany()
                .HasForeignKey(x => x.OrganizerItemId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.OrganizerUser)
                .WithMany()
                .HasForeignKey(x => x.OrganizerUserId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.InviteeUser)
                .WithMany(x => x.ReceivedCalendarInvitations)
                .HasForeignKey(x => x.InviteeUserId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.InviteeItem)
                .WithMany()
                .HasForeignKey(x => x.InviteeItemId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ---------- Seed: Integration Google + Atlassian ----------
        b.Entity<Integration>().HasData(
            new Integration
            {
                Id = GoogleIntegrationId,
                Key = "google",
                DisplayName = "Google Workspace",
                IconUrl = "https://www.google.com/favicon.ico",
                Description = "Gmail · Calendar · Drive",
                Provider = "Google",
                AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
                TokenEndpoint = "https://oauth2.googleapis.com/token",
                SupportedServices = "[\"Gmail\",\"GCal\",\"Drive\"]",
                IsEnabled = true
            },
            // Atlassian / Jira (SCRUM-54). Bật mặc định (migration EnableJiraIntegration).
            // LƯU Ý: cần config OAuth:atlassian:ClientId/Secret thì connect Jira mới hoạt động thật;
            // thiếu creds thì integration hiện nhưng OAuth sẽ lỗi. Có thể tắt runtime qua admin toggle (SCRUM-40).
            new Integration
            {
                Id = AtlassianIntegrationId,
                Key = "atlassian",
                DisplayName = "Atlassian Jira",
                IconUrl = "https://www.atlassian.com/favicon.ico",
                Description = "Jira",
                Provider = "Atlassian",
                AuthorizationEndpoint = "https://auth.atlassian.com/authorize",
                TokenEndpoint = "https://auth.atlassian.com/oauth/token",
                SupportedServices = "[\"Jira\"]",
                IsEnabled = true
            });
    }
}
