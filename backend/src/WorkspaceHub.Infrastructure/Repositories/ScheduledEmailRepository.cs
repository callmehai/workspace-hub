using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
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

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScheduledEmail>> GetPendingDueEmailsAsync(DateTime nowUtc, int maxBatch, CancellationToken ct = default)
    {
        // Tracked: service sẽ cập nhật Status/SentAt/RetryCount/LastError rồi SaveChanges.
        return await Set
            .Where(se => se.Status == ScheduledEmailStatus.Pending && se.SendAt <= nowUtc)
            .OrderBy(se => se.SendAt)
            .Take(maxBatch)
            .ToListAsync(ct);
    }
}
