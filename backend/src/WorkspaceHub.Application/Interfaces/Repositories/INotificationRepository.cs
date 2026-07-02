using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Repositories;

public interface INotificationRepository : IGenericRepository<Notification>
{
    Task<IReadOnlyList<Notification>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    Task<Notification?> GetByIdForUserAsync(Guid userId, Guid notificationId, CancellationToken ct = default);

    Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default);
}
