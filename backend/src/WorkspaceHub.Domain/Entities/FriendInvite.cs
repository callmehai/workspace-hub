namespace WorkspaceHub.Domain.Entities;

/// <summary>
/// Lời mời kết bạn gửi tới email CHƯA có tài khoản. Người nhận bấm link
/// /register?inviteToken= → tạo tài khoản → tự động thành bạn bè với inviter
/// (đã bấm link = đồng ý). Đăng ký cùng email nhưng KHÔNG qua link → chuyển
/// thành lời mời pending trong app để họ tự chấp nhận.
/// </summary>
public class FriendInvite
{
    public Guid Id { get; set; }
    public Guid InviterUserId { get; set; }

    /// <summary>Email người được mời (lowercase). 1 inviter chỉ có 1 invite sống / email.</summary>
    public string Email { get; set; } = null!;

    /// <summary>Token ngẫu nhiên nhúng vào link đăng ký. UNIQUE.</summary>
    public string Token { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }   // null = chưa dùng

    // Navigation
    public User Inviter { get; set; } = null!;
}
