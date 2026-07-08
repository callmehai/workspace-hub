using WorkspaceHub.Application.DTOs.Notifications;

namespace WorkspaceHub.Application.Interfaces.Hubs;

public interface INotificationHubClient
{
    Task ReceiveNotification(NotificationDto notification);
}
