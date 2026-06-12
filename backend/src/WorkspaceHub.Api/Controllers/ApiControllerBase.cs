using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Application.Common;

namespace WorkspaceHub.Api.Controllers;

/// <summary>
/// Base controller duy nhất cho mọi feature controller.
/// Cung cấp route convention "api/[controller]" và helper lấy UserId/Role từ JWT claim.
/// CONVENTIONS.md: "Lấy UserId từ JWT claim qua một base controller / helper,
/// không tin tham số client gửi."
/// </summary>
[ApiController]
[Route("api/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// Lấy UserId (Guid) từ JWT claim "sub".
    /// Throw UnauthorizedException (middleware map → 401) nếu claim thiếu/sai —
    /// chỉ xảy ra khi [Authorize] bị bỏ sót.
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
