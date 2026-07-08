using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace WorkspaceHub.Api.Hubs;

/// <summary>
/// SignalR dùng IUserIdProvider mặc định đọc ClaimTypes.NameIdentifier.
/// JWT của app dùng claim "sub" — mirror logic ApiControllerBase.
/// </summary>
public class SubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? connection.User?.FindFirstValue("sub");
}
