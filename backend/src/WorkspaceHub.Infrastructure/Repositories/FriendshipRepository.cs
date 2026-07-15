using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Infrastructure.Repositories;

public class FriendshipRepository : GenericRepository<Friendship>, IFriendshipRepository
{
    public FriendshipRepository(AppDbContext db) : base(db) { }

    public async Task<Friendship?> GetBetweenAsync(Guid userA, Guid userB, CancellationToken ct = default)
        => await Set.FirstOrDefaultAsync(f =>
            (f.RequesterId == userA && f.AddresseeId == userB) ||
            (f.RequesterId == userB && f.AddresseeId == userA), ct);

    public async Task<IReadOnlyList<Friendship>> GetAllForUserAsync(Guid userId, CancellationToken ct = default)
        => await Set.AsNoTracking()
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .Where(f => f.RequesterId == userId || f.AddresseeId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);

    public async Task<Friendship?> GetByIdWithUsersAsync(Guid id, CancellationToken ct = default)
        => await Set
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .FirstOrDefaultAsync(f => f.Id == id, ct);
}
