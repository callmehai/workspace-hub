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
}
