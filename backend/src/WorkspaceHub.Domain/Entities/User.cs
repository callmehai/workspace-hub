using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Domain.Entities;

/// <summary>Tài khoản local. Không lưu mật khẩu thật, chỉ BCrypt hash (cost 12).</summary>
public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string? PasswordHash { get; set; }
    public string FullName { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public string? LockedReason { get; set; }
    public DateTime? LastLoginAt { get; set; }

    /// <summary>1 user = 1 role (Admin/User). Mặc định User khi register.</summary>
    public UserRole Role { get; set; } = UserRole.User;

    /// <summary>How the user authenticates (Local / Google / Both). Default Local for existing users.</summary>
    public AuthProvider AuthProvider { get; set; } = AuthProvider.Local;

    /// <summary>Google OAuth subject identifier (unique per Google account). Null for local-only users.</summary>
    public string? GoogleSub { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation
    public ICollection<Connection> Connections { get; set; } = new List<Connection>();
    public ICollection<Folder> Folders { get; set; } = new List<Folder>();
    public ICollection<Item> Items { get; set; } = new List<Item>();
    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    public ICollection<ImportantContact> ImportantContacts { get; set; } = new List<ImportantContact>();
    public ICollection<ScheduledEmail> ScheduledEmails { get; set; } = new List<ScheduledEmail>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
