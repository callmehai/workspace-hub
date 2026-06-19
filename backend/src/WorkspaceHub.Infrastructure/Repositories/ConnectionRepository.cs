using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Infrastructure.Repositories;

/// <summary>EF Core implementation của IConnectionRepository (mô hình B).</summary>
public class ConnectionRepository : GenericRepository<Connection>, IConnectionRepository
{
    public ConnectionRepository(AppDbContext db) : base(db) { }

    public async Task<Connection?> GetByUniqueKeyAsync(
        Guid userId,
        ProviderType provider,
        ServiceType serviceType,
        string providerAccountId,
        CancellationToken ct = default)
        => await Set.FirstOrDefaultAsync(c =>
                c.UserId == userId &&
                c.Provider == provider &&
                c.ServiceType == serviceType &&
                c.ProviderAccountId == providerAccountId, ct);

    public async Task<IReadOnlyList<Connection>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await Set
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

    public async Task<Connection?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default)
        => await Set.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Connection>> GetActiveConnectionsToSyncAsync(CancellationToken ct = default)
        => await Set
            .Include(c => c.Integration)
            .Where(c => c.Status == ConnectionStatus.Active && c.Integration.IsEnabled)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Connection>> GetActiveConnectionsForUserAsync(Guid userId, CancellationToken ct = default)
        => await Set
            .Include(c => c.Integration)
            .Where(c => c.UserId == userId && c.Status == ConnectionStatus.Active && c.Integration.IsEnabled)
            .ToListAsync(ct);
}

