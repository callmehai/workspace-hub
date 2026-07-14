using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Infrastructure.Repositories;

public class CalendarInvitationRepository : GenericRepository<CalendarInvitation>, ICalendarInvitationRepository
{
    public CalendarInvitationRepository(AppDbContext db) : base(db) { }

    private IQueryable<CalendarInvitation> WithEvent() => Set
        .Include(x => x.OrganizerItem)
        .Include(x => x.OrganizerUser)
        .Include(x => x.InviteeItem);

    public Task<CalendarInvitation?> GetByIdForInviteeAsync(Guid id, Guid inviteeUserId, CancellationToken ct = default)
        => WithEvent().FirstOrDefaultAsync(x => x.Id == id && x.InviteeUserId == inviteeUserId, ct);

    public Task<List<CalendarInvitation>> GetByOrganizerItemAsync(Guid organizerItemId, CancellationToken ct = default)
        => WithEvent().Where(x => x.OrganizerItemId == organizerItemId).ToListAsync(ct);

    public Task<CalendarInvitation?> GetByICalUidAndEmailAsync(string iCalUid, string email, CancellationToken ct = default)
        => WithEvent().FirstOrDefaultAsync(x => x.ICalUid == iCalUid && x.InviteeEmail == email, ct);

    public Task<List<CalendarInvitation>> GetForInviteeAsync(Guid inviteeUserId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var query = WithEvent().AsNoTracking().Where(x => x.InviteeUserId == inviteeUserId);
        if (from.HasValue)
            query = query.Where(x => (x.OrganizerItem.DueAt ?? x.OrganizerItem.OccurredAt) > from.Value);
        if (to.HasValue)
            query = query.Where(x => x.OrganizerItem.OccurredAt < to.Value);
        return query.OrderBy(x => x.OrganizerItem.OccurredAt).ToListAsync(ct);
    }
}
