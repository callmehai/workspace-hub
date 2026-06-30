using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Infrastructure.Repositories;

public class ImportantContactRepository : GenericRepository<ImportantContact>, IImportantContactRepository
{
    public ImportantContactRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<string>> GetIdentifiersAsync(Guid userId, ImportantContactType type, CancellationToken ct = default)
    {
        return await Set.AsNoTracking()
            .Where(ic => ic.UserId == userId && ic.Type == type)
            .Select(ic => ic.Identifier)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ImportantContact>> GetByUserAsync(Guid userId, ImportantContactType? type = null, CancellationToken ct = default)
    {
        var query = Set.AsNoTracking().Where(ic => ic.UserId == userId);
        if (type.HasValue)
            query = query.Where(ic => ic.Type == type.Value);

        return await query.OrderBy(ic => ic.Label).ToListAsync(ct);
    }

    public async Task<bool> ExistsAsync(Guid userId, ImportantContactType type, string identifier, CancellationToken ct = default)
    {
        return await Set.AsNoTracking()
            .AnyAsync(ic => ic.UserId == userId && ic.Type == type && ic.Identifier == identifier, ct);
    }

    public async Task<ImportantContact?> GetByIdAndUserAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        return await Set.FirstOrDefaultAsync(ic => ic.Id == id && ic.UserId == userId, ct);
    }
}
