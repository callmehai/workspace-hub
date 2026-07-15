using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Repositories;

public interface IFriendshipRepository : IGenericRepository<Friendship>
{
    /// <summary>Tìm quan hệ giữa 2 user BẤT KỂ chiều (tracked — dùng cho accept/update).</summary>
    Task<Friendship?> GetBetweenAsync(Guid userA, Guid userB, CancellationToken ct = default);

    /// <summary>Toàn bộ quan hệ (pending + accepted) mà user tham gia, include cả 2 phía User (AsNoTracking).</summary>
    Task<IReadOnlyList<Friendship>> GetAllForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Lấy theo Id, include Requester + Addressee (tracked).</summary>
    Task<Friendship?> GetByIdWithUsersAsync(Guid id, CancellationToken ct = default);
}
