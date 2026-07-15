using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Repositories;

public interface IFriendInviteRepository : IGenericRepository<FriendInvite>
{
    /// <summary>Invite còn sống (chưa consume) của (inviter, email) — tracked, để refresh token/hạn khi mời lại.</summary>
    Task<FriendInvite?> GetActiveAsync(Guid inviterUserId, string email, CancellationToken ct = default);

    /// <summary>Tra theo token, include Inviter (tracked).</summary>
    Task<FriendInvite?> GetByTokenAsync(string token, CancellationToken ct = default);

    /// <summary>Mọi invite chưa consume gửi tới email này (tracked) — consume khi email đó đăng ký tài khoản.</summary>
    Task<IReadOnlyList<FriendInvite>> GetPendingByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>Invite chưa consume user này đã gửi (AsNoTracking — cho trang Bạn bè).</summary>
    Task<IReadOnlyList<FriendInvite>> GetPendingByInviterAsync(Guid inviterUserId, CancellationToken ct = default);
}
