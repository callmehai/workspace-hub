using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Infrastructure.Repositories;

public class GoogleContactRepository : IGoogleContactRepository
{
    private readonly AppDbContext _db;

    public GoogleContactRepository(AppDbContext db) => _db = db;

    public async Task ReplaceAllForConnectionAsync(Guid connectionId, IReadOnlyList<GoogleContact> contacts, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var existing = await _db.GoogleContacts
            .Where(c => c.ConnectionId == connectionId)
            .ToListAsync(ct);
        _db.GoogleContacts.RemoveRange(existing);

        if (contacts.Count > 0)
            await _db.GoogleContacts.AddRangeAsync(contacts, ct);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<GoogleContact>> GetByConnectionAsync(Guid connectionId, CancellationToken ct = default)
    {
        return await _db.GoogleContacts.AsNoTracking()
            .Where(c => c.ConnectionId == connectionId)
            .OrderBy(c => c.DisplayName ?? c.Email)
            .ThenBy(c => c.Email)
            .ToListAsync(ct);
    }
}
