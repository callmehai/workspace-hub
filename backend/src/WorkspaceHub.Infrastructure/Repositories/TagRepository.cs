using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Infrastructure.Repositories;

public class TagRepository : GenericRepository<Tag>, ITagRepository
{
    public TagRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<(Tag Tag, int ItemCount)>> GetByUserWithCountAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await Set.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.Name)
            .Select(t => new { Tag = t, ItemCount = t.TagAssignments.Count })
            .ToListAsync(ct);

        return rows.Select(r => (r.Tag, r.ItemCount)).ToList();
    }

    public async Task<Tag?> GetByIdAndUserAsync(Guid id, Guid userId, CancellationToken ct = default)
        => await Set.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);

    public async Task<bool> NameExistsAsync(Guid userId, string name, Guid? excludeId = null, CancellationToken ct = default)
        => await Set.AsNoTracking()
            .AnyAsync(t => t.UserId == userId
                        && t.Name.ToLower() == name.ToLower()
                        && (excludeId == null || t.Id != excludeId.Value), ct);

    public async Task<bool> AssignmentExistsAsync(Guid tagId, Guid itemId, CancellationToken ct = default)
        => await Db.TagAssignments.AsNoTracking().AnyAsync(a => a.TagId == tagId && a.ItemId == itemId, ct);

    public async Task AddAssignmentAsync(TagAssignment assignment, CancellationToken ct = default)
        => await Db.TagAssignments.AddAsync(assignment, ct);

    public async Task<TagAssignment?> GetAssignmentAsync(Guid tagId, Guid itemId, CancellationToken ct = default)
        => await Db.TagAssignments.FirstOrDefaultAsync(a => a.TagId == tagId && a.ItemId == itemId, ct);

    public void RemoveAssignment(TagAssignment assignment)
        => Db.TagAssignments.Remove(assignment);
}
