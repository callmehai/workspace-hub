using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.DTOs.Admin;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>
/// Admin endpoints cho dashboard quản trị.
/// [Authorize(Roles = "Admin")] ở class level → mọi action đều yêu cầu JWT role=Admin.
/// User thường (role=User) nhận 403 Forbidden tự động từ ASP.NET Core authorization.
///
/// Route: [Route("api/admin")] — KHÔNG dùng api/[controller] vì controller tên AdminController
/// sẽ map thành api/admin đúng rồi, nhưng đặt tường minh để tránh nhầm.
/// CONVENTIONS.md: "Base controller duy nhất là ApiControllerBase — không tạo base thứ hai".
/// </summary>
[Authorize(Roles = "Admin")]
[Route("api/admin")]
public class AdminController : ApiControllerBase
{
    private readonly IAdminService _adminService;
    private readonly IValidator<GetAdminUsersRequest> _validator;

    public AdminController(
        IAdminService adminService,
        IValidator<GetAdminUsersRequest> validator)
    {
        _adminService = adminService;
        _validator    = validator;
    }

    /// <summary>GET /api/admin/users — danh sách user phân trang, Admin only.</summary>
    /// <remarks>
    /// Query params: search (Email|FullName, case-insensitive), page (≥1), limit (1–100).
    /// Response: { items[], total, page, limit } — cùng envelope với GET /api/items.
    /// </remarks>
    [HttpGet("users")]
    public async Task<ActionResult<PagedResult<AdminUserDto>>> GetUsers(
        [FromQuery] GetAdminUsersRequest request,
        CancellationToken ct)
    {
        // FluentValidation — 400 khi vi phạm; ExceptionMiddleware format chuẩn.
        await _validator.ValidateAndThrowAsync(request, ct);
        return Ok(await _adminService.GetUsersAsync(request, ct));
    }

    /// <summary>GET /api/admin/stats — thống kê hệ thống, Admin only.</summary>
    /// <remarks>
    /// Trả tổng số user/connection/item và số sync error trong 24h gần nhất.
    /// Không cần validate (không có query param).
    /// </remarks>
    [HttpGet("stats")]
    public async Task<ActionResult<AdminStatsDto>> GetStats(CancellationToken ct)
        => Ok(await _adminService.GetStatsAsync(ct));

    /// <summary>POST /api/admin/users/{id}/toggle-active — toggle lock/unlock user, Admin only.</summary>
    [HttpPost("users/{id:guid}/toggle-active")]
    public async Task<ActionResult<AdminUserDto>> ToggleUserActive(Guid id, CancellationToken ct)
        => Ok(await _adminService.ToggleUserActiveAsync(id, CurrentUserId, ct));
}
