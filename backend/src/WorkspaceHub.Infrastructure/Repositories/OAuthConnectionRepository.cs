using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Infrastructure.Repositories;

/// <summary>EF Core implementation của IOAuthConnectionRepository.</summary>
public class OAuthConnectionRepository : GenericRepository<OAuthConnection>, IOAuthConnectionRepository
{
    public OAuthConnectionRepository(AppDbContext db) : base(db) { }

    public async Task<OAuthConnection?> GetByUniqueKeyAsync(
        Guid userId,
        Guid integrationId,
        string providerAccountId,
        CancellationToken ct = default)
        => await Set.AsNoTracking()
                    .FirstOrDefaultAsync(o =>
                        o.UserId == userId &&
                        o.IntegrationId == integrationId &&
                        o.ProviderAccountId == providerAccountId, ct);
}
