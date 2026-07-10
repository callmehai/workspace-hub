using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Infrastructure.Repositories;

public class GoogleContactRepository : IGoogleContactRepository
{
    private readonly AppDbContext _db;

    public GoogleContactRepository(AppDbContext db) => _db = db;

    public async Task SyncForConnectionAsync(Guid connectionId, IReadOnlyList<GoogleContact> incoming, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var existing = await _db.GoogleContacts
            .Where(c => c.ConnectionId == connectionId)
            .ToListAsync(ct);

        var byResource = existing
            .Where(c => !string.IsNullOrWhiteSpace(c.ExternalResourceName))
            .GroupBy(c => c.ExternalResourceName!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var byEmail = existing
            .GroupBy(c => c.Email, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matchedIds = new HashSet<Guid>();

        foreach (var row in incoming)
        {
            GoogleContact? match = null;
            if (!string.IsNullOrWhiteSpace(row.ExternalResourceName)
                && byResource.TryGetValue(row.ExternalResourceName, out var byRes))
            {
                match = byRes;
            }
            else if (byEmail.TryGetValue(row.Email, out var byEm))
            {
                match = byEm;
            }

            if (match != null)
            {
                matchedIds.Add(match.Id);
                match.Email = row.Email;
                match.DisplayName = row.DisplayName;
                match.Source = row.Source;
                match.ExternalResourceName = row.ExternalResourceName;
                match.Etag = row.Etag;
                match.SyncedAt = row.SyncedAt;
            }
            else
            {
                await _db.GoogleContacts.AddAsync(row, ct);
                matchedIds.Add(row.Id);
                if (!string.IsNullOrWhiteSpace(row.ExternalResourceName))
                    byResource[row.ExternalResourceName] = row;
                byEmail[row.Email] = row;
            }
        }

        var orphans = existing.Where(e => !matchedIds.Contains(e.Id)).ToList();
        if (orphans.Count > 0)
            _db.GoogleContacts.RemoveRange(orphans);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public IQueryable<ContactDto> GetQueryableByConnectionId(Guid connectionId) =>
        _db.GoogleContacts.AsNoTracking()
            .Where(c => c.ConnectionId == connectionId)
            .Select(c => new ContactDto
            {
                Id = c.Id,
                ConnectionId = c.ConnectionId,
                Email = c.Email,
                DisplayName = c.DisplayName,
                Source = c.Source,
                Etag = c.Etag,
                SyncedAt = c.SyncedAt,
                UpdatedAt = c.UpdatedAt
            });

    public Task<GoogleContact?> GetByEmailForConnectionAsync(Guid connectionId, string email, CancellationToken ct = default) =>
        _db.GoogleContacts.FirstOrDefaultAsync(
            c => c.ConnectionId == connectionId && c.Email == email, ct);

    public Task<GoogleContact?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken ct = default) =>
        _db.GoogleContacts
            .Join(_db.Connections,
                c => c.ConnectionId,
                conn => conn.Id,
                (c, conn) => new { Contact = c, conn.UserId })
            .Where(x => x.Contact.Id == id && x.UserId == userId)
            .Select(x => x.Contact)
            .FirstOrDefaultAsync(ct);

    public async Task UpsertAsync(GoogleContact contact, CancellationToken ct = default)
    {
        var tracked = await _db.GoogleContacts.FirstOrDefaultAsync(c => c.Id == contact.Id, ct);
        if (tracked == null)
        {
            await _db.GoogleContacts.AddAsync(contact, ct);
        }
        else
        {
            tracked.Email = contact.Email;
            tracked.DisplayName = contact.DisplayName;
            tracked.Source = contact.Source;
            tracked.ExternalResourceName = contact.ExternalResourceName;
            tracked.Etag = contact.Etag;
            tracked.SyncedAt = contact.SyncedAt;
            tracked.UpdatedAt = contact.UpdatedAt;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(GoogleContact contact, CancellationToken ct = default)
    {
        _db.GoogleContacts.Remove(contact);
        await _db.SaveChangesAsync(ct);
    }
}

