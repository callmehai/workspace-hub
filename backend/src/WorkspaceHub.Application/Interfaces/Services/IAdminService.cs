using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.DTOs.Admin;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// Nghiệp vụ admin: danh sách user có phân trang + thống kê hệ thống.
/// Chỉ được inject vào AdminController (yêu cầu Role = Admin ở class level).
/// </summary>
public interface IAdminService
{
    /// <summary>
    /// Danh sách user phân trang cho admin dashboard.
    /// Search khớp Email HOẶC FullName (case-insensitive — SQL Server collation).
    /// Mỗi DTO có ConnectionCount và ItemCount (EF projection, không N+1).
    /// </summary>
    Task<PagedResult<AdminUserDto>> GetUsersAsync(
        GetAdminUsersRequest request,
        CancellationToken ct = default);

    Task<AdminUserDto> ToggleUserActiveAsync(Guid id, Guid currentAdminId, CancellationToken ct = default);

    /// <summary>
    /// Thống kê tổng quan hệ thống: tổng user, connection, item, và sync error trong 24h.
    /// </summary>
    Task<AdminStatsDto> GetStatsAsync(CancellationToken ct = default);
}
