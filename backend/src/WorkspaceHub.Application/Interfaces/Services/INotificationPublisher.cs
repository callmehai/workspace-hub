using WorkspaceHub.Application.DTOs.Notifications;

namespace WorkspaceHub.Application.Interfaces.Services;

public interface INotificationPublisher
{
    Task PublishToUserAsync(Guid userId, NotificationDto notification, CancellationToken ct = default);
}
