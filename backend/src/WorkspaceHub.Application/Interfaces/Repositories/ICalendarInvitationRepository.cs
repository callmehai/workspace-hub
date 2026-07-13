using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Repositories;

public interface ICalendarInvitationRepository : IGenericRepository<CalendarInvitation>
{
    Task<CalendarInvitation?> GetByIdForInviteeAsync(Guid id, Guid inviteeUserId, CancellationToken ct = default);
    Task<List<CalendarInvitation>> GetByOrganizerItemAsync(Guid organizerItemId, CancellationToken ct = default);
    Task<CalendarInvitation?> GetByICalUidAndEmailAsync(string iCalUid, string email, CancellationToken ct = default);
    Task<List<CalendarInvitation>> GetForInviteeAsync(Guid inviteeUserId, DateTime? from, DateTime? to, CancellationToken ct = default);
}
