using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace WorkspaceHub.Api.Controllers;

/// <summary>
/// Base controller cho mọi feature controller.
/// Cung cấp route convention [api/controller] và helper lấy UserId từ JWT claim.
/// CONVENTIONS.md: "Lấy UserId từ JWT claim qua một base controller / helper,
/// không tin tham số client gửi."
/// </summary>
[ApiController]
[Route("api/[controller]")]
public abstract class BaseApiController : ControllerBase
{
    /// <summary>
    /// Lấy UserId (Guid) từ JWT claim "sub".
    /// Throw nếu claim thiếu (chỉ xảy ra khi [Authorize] bị bỏ sót).
    /// </summary>
    protected Guid UserId
    {
        get
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.FindFirstValue("sub");

            if (string.IsNullOrEmpty(sub) || !Guid.TryParse(sub, out var userId))
                throw new UnauthorizedAccessException("Missing or invalid 'sub' claim in JWT.");

            return userId;
        }
    }
}
