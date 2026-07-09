using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Notifications;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Repositories;

public interface INotificationRepository : IGenericRepository<Notification>
{
    /// <summary>OData list — EF projection, filter theo user trước khi trả IQueryable.</summary>
    IQueryable<NotificationDto> GetByUserId(Guid userId);

    Task<Notification?> GetByIdForUserAsync(Guid userId, Guid notificationId, CancellationToken ct = default);

    Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default);

    Task AddRangeAsync(IEnumerable<Notification> notifications, CancellationToken ct = default);
}
