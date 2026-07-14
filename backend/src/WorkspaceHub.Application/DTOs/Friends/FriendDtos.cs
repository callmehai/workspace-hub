namespace WorkspaceHub.Application.DTOs.Friends;

/// <summary>Gửi lời mời kết bạn theo email. ConnectionId (Gmail) optional — để gửi mail mời khi email chưa có tài khoản.</summary>
public record SendFriendRequestRequest(string Email, Guid? ConnectionId = null);

/// <summary>
/// Kết quả gửi lời mời. Outcome: RequestSent (user đã có tài khoản — tạo pending) |
/// AutoAccepted (phía kia đã mời mình trước — thành bạn luôn) |
/// InviteCreated (email chưa có tài khoản — tạo invite link, EmailSent = đã gửi được mail mời qua Gmail).
/// </summary>
public record SendFriendRequestResult(
    string Outcome,
    FriendDto? Friend,
    FriendInviteDto? Invite,
    bool EmailSent);

/// <summary>1 quan hệ bạn bè dưới góc nhìn user hiện tại (counterpart = phía bên kia).</summary>
public record FriendDto(
    Guid FriendshipId,
    Guid UserId,
    string Email,
    string FullName,
    string? AvatarUrl,
    string Status,          // Pending | Accepted
    string MyTier,          // Friend | CloseFriend — hạng TÔI đặt cho họ
    bool IsIncoming,        // true = họ mời tôi (tôi là addressee)
    DateTime CreatedAt,
    DateTime? RespondedAt);

/// <summary>Lời mời qua email (người nhận chưa có tài khoản).</summary>
public record FriendInviteDto(
    Guid Id,
    string Email,
    string InviteLink,
    DateTime CreatedAt,
    DateTime ExpiresAt);

/// <summary>Toàn cảnh trang Bạn bè: đã là bạn / mời đến / mời đi / mời qua email.</summary>
public record FriendsOverviewDto(
    IReadOnlyList<FriendDto> Friends,
    IReadOnlyList<FriendDto> IncomingRequests,
    IReadOnlyList<FriendDto> OutgoingRequests,
    IReadOnlyList<FriendInviteDto> EmailInvites);

/// <summary>Đổi hạng bạn bè (Friend | CloseFriend) — chỉ ảnh hưởng phía user hiện tại.</summary>
public record UpdateFriendTierRequest(string Tier);

/// <summary>Thông tin public của 1 invite token — cho banner trang đăng ký ("X mời bạn tham gia").</summary>
public record FriendInvitePublicDto(string InviterName, string Email);
