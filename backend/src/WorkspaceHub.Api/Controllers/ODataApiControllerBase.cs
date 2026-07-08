using System.Security.Claims;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using WorkspaceHub.Application.Common;

namespace WorkspaceHub.Api.Controllers;

/// <summary>Base OData controller — lấy UserId từ JWT giống ApiControllerBase.</summary>
public abstract class ODataApiControllerBase : ODataController
{
    protected Guid CurrentUserId
    {
        get
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.FindFirstValue("sub");

            if (string.IsNullOrEmpty(sub) || !Guid.TryParse(sub, out var userId))
                throw new UnauthorizedException("Invalid or missing user identity in token");

            return userId;
        }
    }
}
