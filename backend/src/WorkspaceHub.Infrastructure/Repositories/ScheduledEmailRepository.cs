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
        var emails = await Set.Where(se => se.ConnectionId == connectionId).ToListAsync(ct);
        Set.RemoveRange(emails);
    }
}
