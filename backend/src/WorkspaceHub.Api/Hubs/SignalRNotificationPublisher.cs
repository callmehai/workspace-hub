using Microsoft.AspNetCore.SignalR;
using WorkspaceHub.Application.DTOs.Notifications;
using WorkspaceHub.Application.Interfaces.Hubs;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Hubs;

public class SignalRNotificationPublisher : INotificationPublisher
{
    private readonly IHubContext<NotificationHub, INotificationHubClient> _hub;

    public SignalRNotificationPublisher(IHubContext<NotificationHub, INotificationHubClient> hub)
    {
        _hub = hub;
    }

    public Task PublishToUserAsync(
        Guid userId, 
        NotificationDto notification, 
        CancellationToken ct = default)
        => _hub.Clients.User(userId.ToString()).ReceiveNotification(notification);

    public Task PublishToUsersAsync(
        IReadOnlyList<Guid> userIds,
        NotificationDto notification,
        CancellationToken ct = default)
    {
        if (userIds.Count == 0)
            return Task.CompletedTask;

        var ids = userIds.Select(id => id.ToString()).ToList();
        return _hub.Clients.Users(ids).ReceiveNotification(notification);
    }
}
