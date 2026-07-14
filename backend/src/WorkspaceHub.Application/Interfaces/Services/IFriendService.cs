using WorkspaceHub.Application.DTOs.Friends;

namespace WorkspaceHub.Application.Interfaces.Services;

public interface IFriendService
{
    /// <summary>Trang Bạn bè: accepted + pending 2 chiều + invite email đã gửi.</summary>
    Task<FriendsOverviewDto> GetOverviewAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Gửi lời mời kết bạn theo email. Email đã có tài khoản → tạo Pending (+notification);
    /// phía kia đã mời mình trước → auto-accept; email chưa có tài khoản → tạo FriendInvite
    /// + cố gắng gửi mail mời qua Gmail connection của user (fail vẫn trả link để copy).
    /// </summary>
    Task<SendFriendRequestResult> SendRequestAsync(Guid userId, SendFriendRequestRequest request, CancellationToken ct = default);

    /// <summary>Chấp nhận lời mời — chỉ addressee của friendship Pending.</summary>
    Task<FriendDto> AcceptAsync(Guid userId, Guid friendshipId, CancellationToken ct = default);

    /// <summary>Xoá quan hệ: Pending = từ chối/hủy lời mời; Accepted = unfriend. Cả 2 phía đều gọi được.</summary>
    Task RemoveAsync(Guid userId, Guid friendshipId, CancellationToken ct = default);

    /// <summary>Đổi hạng bạn bè phía user hiện tại (chỉ friendship Accepted).</summary>
    Task<FriendDto> SetTierAsync(Guid userId, Guid friendshipId, UpdateFriendTierRequest request, CancellationToken ct = default);

    /// <summary>Hủy invite email chưa consume do mình gửi.</summary>
    Task CancelInviteAsync(Guid userId, Guid inviteId, CancellationToken ct = default);

    /// <summary>Thông tin public của invite token (banner trang đăng ký). 404 nếu token sai/hết hạn/đã dùng.</summary>
    Task<FriendInvitePublicDto> GetInviteByTokenAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Gọi khi 1 tài khoản mới được tạo (register local hoặc Google Sign-In lần đầu):
    /// token khớp → thành bạn NGAY với inviter (đã bấm link = đồng ý); các invite khác
    /// trùng email → chuyển thành lời mời Pending trong app. Best-effort — không được fail đăng ký.
    /// </summary>
    Task ConsumeInvitesOnRegistrationAsync(Guid newUserId, string email, string? inviteToken, CancellationToken ct = default);
}
