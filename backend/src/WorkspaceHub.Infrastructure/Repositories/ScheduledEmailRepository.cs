using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Infrastructure.Repositories;

/// <summary>EF Core implementation của IScheduledEmailRepository.</summary>
public class ScheduledEmailRepository : GenericRepository<ScheduledEmail>, IScheduledEmailRepository
{
    public ScheduledEmailRepository(AppDbContext db) : base(db) { }

    /// <inheritdoc/>
    public async Task DeleteByConnectionIdAsync(Guid connectionId, CancellationToken ct = default)
    {
        await Set
            .Where(se => se.ConnectionId == connectionId)
            .ExecuteDeleteAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScheduledEmail>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        return await Set.AsNoTracking()
            .Where(se => se.UserId == userId)
            .OrderByDescending(se => se.SendAt)
            .ToListAsync(ct);
    }
}
