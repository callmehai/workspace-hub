using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Domain.Entities;

/// <summary>
/// Quan hệ bạn bè NỘI BỘ app (không dùng provider ngoài). 1 row / 1 cặp user,
/// chiều xác định bởi RequesterId (người gửi lời mời). Decline/hủy/unfriend = xoá row.
/// Tier lưu riêng từng phía (A coi B là bạn thân không bắt buộc ngược lại).
/// </summary>
public class Friendship
{
    public Guid Id { get; set; }
    public Guid RequesterId { get; set; }
    public Guid AddresseeId { get; set; }
    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;

    /// <summary>Hạng bạn bè theo góc nhìn của Requester.</summary>
    public FriendTier RequesterTier { get; set; } = FriendTier.Friend;

    /// <summary>Hạng bạn bè theo góc nhìn của Addressee.</summary>
    public FriendTier AddresseeTier { get; set; } = FriendTier.Friend;

    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }   // null = chưa accept

    // Navigation
    public User Requester { get; set; } = null!;
    public User Addressee { get; set; } = null!;
}
