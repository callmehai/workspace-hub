using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Application.Common;

namespace WorkspaceHub.Api.Controllers;

/// <summary>
/// Base controller cho mọi endpoint cần auth.
/// Cung cấp CurrentUserId (từ JWT claim "sub") và CurrentUserRole.
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// Extracts UserId from JWT claim "sub".
    /// Throws UnauthorizedException if the claim is missing or invalid.
    /// Only call this from endpoints decorated with [Authorize].
    /// </summary>
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

    /// <summary>
    /// Returns the role claim value from the JWT ("Admin" or "User").
    /// </summary>
    protected string? CurrentUserRole =>
        User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");
}
