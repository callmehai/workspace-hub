using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Application.DTOs.Friends;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

/// <summary>
/// Bạn bè NỘI BỘ app — không đụng provider ngoài. Kết bạn theo email:
/// đã có tài khoản → Pending trong app; chưa có → FriendInvite + mail mời
/// (gửi qua chính Gmail connection của người mời) chứa link /register?inviteToken=.
/// </summary>
public class FriendService : IFriendService
{
    private const int InviteExpiryDays = 14;

    private readonly IFriendshipRepository _friendships;
    private readonly IFriendInviteRepository _invites;
    private readonly IUserRepository _users;
    private readonly IConnectionRepository _connections;
    private readonly ISendEmailService _sendEmail;
    private readonly INotificationService _notifications;
    private readonly IConfiguration _config;
    private readonly ILogger<FriendService> _logger;

    public FriendService(
        IFriendshipRepository friendships,
        IFriendInviteRepository invites,
        IUserRepository users,
        IConnectionRepository connections,
        ISendEmailService sendEmail,
        INotificationService notifications,
        IConfiguration config,
        ILogger<FriendService> logger)
    {
        _friendships = friendships;
        _invites = invites;
        _users = users;
        _connections = connections;
        _sendEmail = sendEmail;
        _notifications = notifications;
        _config = config;
        _logger = logger;
    }

    public async Task<FriendsOverviewDto> GetOverviewAsync(Guid userId, CancellationToken ct = default)
    {
        var all = await _friendships.GetAllForUserAsync(userId, ct);
        var invites = await _invites.GetPendingByInviterAsync(userId, ct);

        var friends = new List<FriendDto>();
        var incoming = new List<FriendDto>();
        var outgoing = new List<FriendDto>();
        foreach (var f in all)
        {
            var dto = MapToDto(f, userId);
            if (f.Status == FriendshipStatus.Accepted) friends.Add(dto);
            else if (dto.IsIncoming) incoming.Add(dto);
            else outgoing.Add(dto);
        }

        var inviteDtos = invites
            .Where(i => i.ExpiresAt > DateTime.UtcNow)
            .Select(i => new FriendInviteDto(i.Id, i.Email, BuildInviteLink(i.Token), i.CreatedAt, i.ExpiresAt))
            .ToList();

        return new FriendsOverviewDto(friends, incoming, outgoing, inviteDtos);
    }

    public async Task<SendFriendRequestResult> SendRequestAsync(Guid userId, SendFriendRequestRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var me = await _users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException("User", userId);

        if (string.Equals(me.Email, email, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("Không thể kết bạn với chính mình.");

        var target = await _users.GetByEmailAsync(email, ct);

        // ── Email đã có tài khoản → friendship trong app ──
        if (target is not null)
        {
            var existing = await _friendships.GetBetweenAsync(userId, target.Id, ct);
            if (existing is not null)
            {
                if (existing.Status == FriendshipStatus.Accepted)
                    throw new ConflictException("Hai bạn đã là bạn bè.");

                // Mình mời rồi → chặn spam. Phía kia mời mình trước → cả 2 cùng muốn = accept luôn.
                if (existing.RequesterId == userId)
                    throw new ConflictException("Đã gửi lời mời trước đó — chờ phía bên kia chấp nhận.");

                existing.Status = FriendshipStatus.Accepted;
                existing.RespondedAt = DateTime.UtcNow;
                await _friendships.SaveChangesAsync(ct);
                await NotifySafeAsync(existing.RequesterId, NotificationType.FriendAccepted,
                    "notifications.friendAccepted", me, ct);
                var accepted = await _friendships.GetByIdWithUsersAsync(existing.Id, ct);
                return new SendFriendRequestResult("AutoAccepted", MapToDto(accepted!, userId), null, false);
            }

            var friendship = new Friendship
            {
                Id = Guid.NewGuid(),
                RequesterId = userId,
                AddresseeId = target.Id,
                Status = FriendshipStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
            await _friendships.AddAsync(friendship, ct);
            await _friendships.SaveChangesAsync(ct);

            await NotifySafeAsync(target.Id, NotificationType.FriendRequest,
                "notifications.friendRequest", me, ct);

            friendship.Requester = me;
            friendship.Addressee = target;
            return new SendFriendRequestResult("RequestSent", MapToDto(friendship, userId), null, false);
        }

        // ── Email chưa có tài khoản → invite link + mail mời ──
        var invite = await _invites.GetActiveAsync(userId, email, ct);
        if (invite is null)
        {
            invite = new FriendInvite
            {
                Id = Guid.NewGuid(),
                InviterUserId = userId,
                Email = email,
                Token = GenerateToken(),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(InviteExpiryDays)
            };
            await _invites.AddAsync(invite, ct);
        }
        else
        {
            // Mời lại → gia hạn, giữ token (link cũ đã gửi vẫn sống).
            invite.ExpiresAt = DateTime.UtcNow.AddDays(InviteExpiryDays);
        }
        await _invites.SaveChangesAsync(ct);

        var link = BuildInviteLink(invite.Token);
        var emailSent = await TrySendInviteEmailAsync(me, email, link, request.ConnectionId, ct);

        var inviteDto = new FriendInviteDto(invite.Id, invite.Email, link, invite.CreatedAt, invite.ExpiresAt);
        return new SendFriendRequestResult("InviteCreated", null, inviteDto, emailSent);
    }

    public async Task<FriendDto> AcceptAsync(Guid userId, Guid friendshipId, CancellationToken ct = default)
    {
        var friendship = await GetOwnedAsync(userId, friendshipId, ct);

        if (friendship.Status != FriendshipStatus.Pending)
            throw new ConflictException("Lời mời đã được xử lý.");
        if (friendship.AddresseeId != userId)
            throw new ForbiddenException("Chỉ người nhận lời mời mới chấp nhận được.");

        friendship.Status = FriendshipStatus.Accepted;
        friendship.RespondedAt = DateTime.UtcNow;
        await _friendships.SaveChangesAsync(ct);

        await NotifySafeAsync(friendship.RequesterId, NotificationType.FriendAccepted,
            "notifications.friendAccepted", friendship.Addressee, ct);

        return MapToDto(friendship, userId);
    }

    public async Task RemoveAsync(Guid userId, Guid friendshipId, CancellationToken ct = default)
    {
        var friendship = await GetOwnedAsync(userId, friendshipId, ct);
        _friendships.Remove(friendship);
        await _friendships.SaveChangesAsync(ct);
    }

    public async Task<FriendDto> SetTierAsync(Guid userId, Guid friendshipId, UpdateFriendTierRequest request, CancellationToken ct = default)
    {
        if (!Enum.TryParse<FriendTier>(request.Tier, ignoreCase: true, out var tier))
            throw new BusinessRuleException($"Tier không hợp lệ: {request.Tier}");

        var friendship = await GetOwnedAsync(userId, friendshipId, ct);
        if (friendship.Status != FriendshipStatus.Accepted)
            throw new ConflictException("Chỉ đặt hạng cho bạn bè đã chấp nhận.");

        if (friendship.RequesterId == userId) friendship.RequesterTier = tier;
        else friendship.AddresseeTier = tier;
        await _friendships.SaveChangesAsync(ct);

        return MapToDto(friendship, userId);
    }

    public async Task CancelInviteAsync(Guid userId, Guid inviteId, CancellationToken ct = default)
    {
        var invite = await _invites.GetByIdAsync(inviteId, ct);
        if (invite is null || invite.InviterUserId != userId)
            throw new NotFoundException("FriendInvite", inviteId);

        _invites.Remove(invite);
        await _invites.SaveChangesAsync(ct);
    }

    public async Task<FriendInvitePublicDto> GetInviteByTokenAsync(string token, CancellationToken ct = default)
    {
        var invite = await _invites.GetByTokenAsync(token, ct);
        if (invite is null || invite.ConsumedAt is not null || invite.ExpiresAt <= DateTime.UtcNow)
            throw new NotFoundException("FriendInvite", token);

        return new FriendInvitePublicDto(invite.Inviter.FullName, invite.Email);
    }

    public async Task ConsumeInvitesOnRegistrationAsync(Guid newUserId, string email, string? inviteToken, CancellationToken ct = default)
    {
        try
        {
            var pending = await _invites.GetPendingByEmailAsync(email.Trim().ToLowerInvariant(), ct);
            if (pending.Count == 0) return;

            var newUser = await _users.GetByIdAsync(newUserId, ct);
            foreach (var invite in pending)
            {
                invite.ConsumedAt = DateTime.UtcNow;
                if (invite.ExpiresAt <= DateTime.UtcNow) continue;
                if (invite.InviterUserId == newUserId) continue;

                var existing = await _friendships.GetBetweenAsync(invite.InviterUserId, newUserId, ct);
                if (existing is not null) continue;

                // Bấm đúng link (token khớp) = đã đồng ý → bạn bè luôn; còn lại → Pending chờ chấp nhận.
                var viaToken = inviteToken is not null && invite.Token == inviteToken;
                await _friendships.AddAsync(new Friendship
                {
                    Id = Guid.NewGuid(),
                    RequesterId = invite.InviterUserId,
                    AddresseeId = newUserId,
                    Status = viaToken ? FriendshipStatus.Accepted : FriendshipStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    RespondedAt = viaToken ? DateTime.UtcNow : null
                }, ct);

                if (viaToken && newUser is not null)
                    await NotifySafeAsync(invite.InviterUserId, NotificationType.FriendAccepted,
                        "notifications.friendAccepted", newUser, ct);
            }
            await _friendships.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Best-effort: lỗi kết bạn không được chặn đăng ký tài khoản.
            _logger.LogError(ex, "Consume friend invites thất bại cho user {UserId}", newUserId);
        }
    }

    // ── private helpers ──────────────────────────────────────────────

    /// <summary>Friendship user tham gia (requester hoặc addressee); ngoài cuộc → 404 (không leak).</summary>
    private async Task<Friendship> GetOwnedAsync(Guid userId, Guid friendshipId, CancellationToken ct)
    {
        var friendship = await _friendships.GetByIdWithUsersAsync(friendshipId, ct);
        if (friendship is null || (friendship.RequesterId != userId && friendship.AddresseeId != userId))
            throw new NotFoundException("Friendship", friendshipId);
        return friendship;
    }

    private static FriendDto MapToDto(Friendship f, Guid currentUserId)
    {
        var isRequester = f.RequesterId == currentUserId;
        var other = isRequester ? f.Addressee : f.Requester;
        return new FriendDto(
            f.Id, other.Id, other.Email, other.FullName, other.AvatarUrl,
            f.Status.ToString(),
            (isRequester ? f.RequesterTier : f.AddresseeTier).ToString(),
            IsIncoming: !isRequester,
            f.CreatedAt, f.RespondedAt);
    }

    private async Task NotifySafeAsync(Guid targetUserId, NotificationType type, string titleKey, User actor, CancellationToken ct)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { from = actor.FullName, preview = actor.Email });
            await _notifications.CreateAndSendAsync(targetUserId, type, titleKey, body, "/friends", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gửi notification {Type} tới {UserId} thất bại", type, targetUserId);
        }
    }

    private string BuildInviteLink(string token)
    {
        var baseUrl = _config["App:FrontendBaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
            baseUrl = _config.GetSection("Cors:AllowedOrigins").Get<string[]>()?.FirstOrDefault()?.TrimEnd('/')
                      ?? "http://localhost:5173";
        return $"{baseUrl}/register?inviteToken={token}";
    }

    private static string GenerateToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(); // 48 hex chars

    /// <summary>Gửi mail mời qua Gmail connection của người mời. Không có connection / lỗi → false (FE hiện link copy).</summary>
    private async Task<bool> TrySendInviteEmailAsync(User inviter, string toEmail, string link, Guid? connectionId, CancellationToken ct)
    {
        try
        {
            var connId = connectionId;
            if (connId is null)
            {
                var conns = await _connections.GetByUserIdAsync(inviter.Id, ct);
                connId = conns.FirstOrDefault(c => c.ServiceType == ServiceType.Gmail && c.Status == ConnectionStatus.Active)?.Id;
            }
            if (connId is null) return false;

            var subject = $"{inviter.FullName} mời bạn tham gia Workspace Hub";
            var bodyHtml = $"""
                <div style="font-family:sans-serif;max-width:520px;margin:0 auto">
                  <h2 style="color:#4f46e5">Workspace Hub</h2>
                  <p><b>{System.Net.WebUtility.HtmlEncode(inviter.FullName)}</b> ({inviter.Email}) mời bạn tham gia Workspace Hub
                     — nơi gom email, lịch, file và công việc về một chỗ.</p>
                  <p>Nhấn nút dưới đây để tạo tài khoản và tự động kết bạn với {System.Net.WebUtility.HtmlEncode(inviter.FullName)}:</p>
                  <p style="text-align:center;margin:24px 0">
                    <a href="{link}" style="background:#4f46e5;color:#fff;padding:12px 24px;border-radius:8px;text-decoration:none;display:inline-block">
                      Tham gia Workspace Hub</a>
                  </p>
                  <p style="color:#64748b;font-size:13px">Hoặc mở link: <a href="{link}">{link}</a><br/>
                     Lời mời hết hạn sau {InviteExpiryDays} ngày.</p>
                </div>
                """;

            await _sendEmail.SendAsync(inviter.Id, new SendEmailRequest
            {
                ConnectionId = connId.Value,
                To = new List<string> { toEmail },
                Subject = subject,
                BodyHtml = bodyHtml
            }, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gửi mail mời kết bạn tới {Email} thất bại", toEmail);
            return false;
        }
    }
}
