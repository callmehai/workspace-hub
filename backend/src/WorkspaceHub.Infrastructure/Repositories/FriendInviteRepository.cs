using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Infrastructure.Repositories;

public class FriendInviteRepository : GenericRepository<FriendInvite>, IFriendInviteRepository
{
    public FriendInviteRepository(AppDbContext db) : base(db) { }

    public async Task<FriendInvite?> GetActiveAsync(Guid inviterUserId, string email, CancellationToken ct = default)
        => await Set.FirstOrDefaultAsync(i =>
            i.InviterUserId == inviterUserId && i.Email == email && i.ConsumedAt == null, ct);

    public async Task<FriendInvite?> GetByTokenAsync(string token, CancellationToken ct = default)
        => await Set.Include(i => i.Inviter).FirstOrDefaultAsync(i => i.Token == token, ct);

    public async Task<IReadOnlyList<FriendInvite>> GetPendingByEmailAsync(string email, CancellationToken ct = default)
        => await Set.Include(i => i.Inviter)
            .Where(i => i.Email == email && i.ConsumedAt == null)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FriendInvite>> GetPendingByInviterAsync(Guid inviterUserId, CancellationToken ct = default)
        => await Set.AsNoTracking()
            .Where(i => i.InviterUserId == inviterUserId && i.ConsumedAt == null)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);
}
