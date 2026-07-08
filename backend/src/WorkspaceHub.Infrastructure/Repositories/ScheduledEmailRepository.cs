using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.DTOs.ScheduledEmails;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Infrastructure.Repositories;

/// <summary>EF Core implementation của IScheduledEmailRepository.</summary>
public class ScheduledEmailRepository : GenericRepository<ScheduledEmail>, IScheduledEmailRepository
{
    public ScheduledEmailRepository(AppDbContext db) : base(db) { }

    /// <summary>
    /// OData in-memory: load rows theo userId rồi map DTO — $filter/$orderby/$top chạy trên memory
    /// (ToJson parse không dịch sang SQL). Dataset scoped theo user nên chấp nhận được MVP.
    /// </summary>
    public IQueryable<ScheduledEmailDto> GetByUserId(Guid userId) =>
        Set.AsNoTracking()
            .Where(se => se.UserId == userId)
            .AsEnumerable()
            .Select(ScheduledEmailMapper.ToDto)
            .AsQueryable();

    /// <inheritdoc/>
    public async Task DeleteByConnectionIdAsync(Guid connectionId, CancellationToken ct = default)
    {
        await Set
            .Where(se => se.ConnectionId == connectionId)
            .ExecuteDeleteAsync(ct);
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
