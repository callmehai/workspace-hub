using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Infrastructure.Repositories;

/// <summary>EF Core implementation của IIntegrationRepository.</summary>
public class IntegrationRepository : GenericRepository<Integration>, IIntegrationRepository
{
    public IntegrationRepository(AppDbContext db) : base(db) { }

    public async Task<Integration?> GetByKeyAsync(string key, CancellationToken ct = default)
        => await Set.AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Key == key, ct);
}
