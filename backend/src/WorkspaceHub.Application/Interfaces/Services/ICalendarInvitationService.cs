using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Services;

public interface ICalendarInvitationService
{
    Task ReconcileOrganizerEventAsync(Item organizerItem, CalendarEvent calendarEvent, CancellationToken ct = default);
    Task ReconcileSyncedEventAsync(Connection connection, Item localItem, CalendarEventDto calendarEvent, CancellationToken ct = default);
    Task<IReadOnlyList<CalendarInvitationResponse>> GetForUserAsync(Guid userId, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<CalendarInvitationResponse> GetAsync(Guid userId, Guid invitationId, CancellationToken ct = default);
    Task<CalendarInvitationResponse> RespondAsync(Guid userId, Guid invitationId, RespondCalendarInvitationRequest request, CancellationToken ct = default);
    Task ClearInviteeItemLinksAsync(IEnumerable<Guid> inviteeItemIds, CancellationToken ct = default);
}
