using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WorkspaceHub.Application.Interfaces.Hubs;

namespace WorkspaceHub.Api.Hubs;

[Authorize]
public class NotificationHub : Hub<INotificationHubClient>
{
}
