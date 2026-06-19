using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.DTOs.Admin;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Infrastructure.Services;

/// <summary>
/// Nghiệp vụ admin — danh sách user + thống kê hệ thống.
///
/// Đặt trong Infrastructure.Services (không phải Application.Services) vì:
///   AdminService cần AppDbContext trực tiếp để thực hiện EF projection phức tạp (1 SQL,
///   no N+1) cho ConnectionCount + ItemCount. Application project không reference Infrastructure,
///   nên đặt service này trong Infrastructure là đúng về dependency direction.
///
/// Implement interface IAdminService (từ Application layer) — Controller inject qua interface,
/// không biết về class cụ thể → đảm bảo loose coupling.
///
/// Lý do không inject IUserRepository:
///   AdminService query _db.Users trực tiếp để có EF anonymous projection gồm cả
///   ConnCount + ItemCount trong 1 SQL duy nhất. Đẩy projection này vào IUserRepository
///   sẽ buộc repository phải biết về DTO (vi phạm separation of concerns).
/// </summary>
public class AdminService : IAdminService
{
    private readonly AppDbContext _db;

    public AdminService(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<PagedResult<AdminUserDto>> GetUsersAsync(
        GetAdminUsersRequest request,
        CancellationToken ct = default)
    {
        // Defense-in-depth clamp (validator đã check, nhưng phòng bypass trực tiếp)
        var page  = Math.Max(1, request.Page);
        var limit = Math.Clamp(request.Limit, 1, 100);
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();

        // ── Single SQL, no N+1: EF project ConnectionCount + ItemCount inline ──
        // Contains(term) → LIKE '%term%' trên SQL Server CI_AS collation = case-insensitive.
        // KHÔNG dùng .ToLower() vì sẽ prevent index usage (CONVENTIONS.md).
        var query = _db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u =>
                u.Email.Contains(search) || u.FullName.Contains(search));
        }

        // EF anonymous projection: Count sub-collections trong cùng 1 SQL query
        var projected = query.Select(u => new
        {
            User      = u,
            ConnCount = u.Connections.Count(),
            ItemCount = u.Items.Count()
        });

        var total = await projected.CountAsync(ct);

        var rows = await projected
            .OrderByDescending(x => x.User.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(ct);

        var dtos = rows.Select(x => new AdminUserDto(
            Id:              x.User.Id,
            Email:           x.User.Email,
            FullName:        x.User.FullName,
            Role:            x.User.Role.ToString(),
            IsActive:        x.User.IsActive,
            LastLoginAt:     x.User.LastLoginAt,
            CreatedAt:       x.User.CreatedAt,
            ConnectionCount: x.ConnCount,
            ItemCount:       x.ItemCount
        )).ToList().AsReadOnly();

        return new PagedResult<AdminUserDto>(dtos, total, page, limit);
    }

    /// <inheritdoc/>
    public async Task<AdminStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        // ── Users ──
        // IsActive = true → active; false → locked (DATABASE.md: "false = khoá").
        // lockedUsers = totalUsers - activeUsers đảm bảo activeUsers + lockedUsers == totalUsers.
        var totalUsers  = await _db.Users.CountAsync(ct);
        var activeUsers = await _db.Users.CountAsync(u => u.IsActive, ct);
        var lockedUsers = totalUsers - activeUsers;

        // ── Connections ──
        var totalConnections = await _db.Connections.CountAsync(ct);

        // GroupBy enum → string key (enum lưu dạng string theo HaveConversion<string>() trong AppDbContext).
        // Dictionary chỉ chứa key có count > 0 (GroupBy tự lọc nhóm trống).
        var byStatus = await _db.Connections
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
            .ToListAsync(ct);

        // IReadOnlyDictionary: record là immutable về reference, nhưng Dictionary bên trong
        // vẫn mutable. Cast sang IReadOnlyDictionary ngăn consumer gọi .Add()/.Remove().
        IReadOnlyDictionary<string, int> connectionsByStatus =
            byStatus.ToDictionary(x => x.Status, x => x.Count);

        // ── Items ──
        var totalItems = await _db.Items.CountAsync(ct);

        // ── Sync errors trong 24h ──
        // Điều kiện kép: Status=Error VÀ LastSyncedAt không null VÀ LastSyncedAt >= cutoff.
        // Null LastSyncedAt → excluded (connection bị Error nhưng chưa bao giờ sync thành công).
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var syncErrorsLast24h = await _db.Connections.CountAsync(c =>
            c.Status == ConnectionStatus.Error &&
            c.LastSyncedAt.HasValue &&
            c.LastSyncedAt.Value >= cutoff, ct);

        return new AdminStatsDto(
            TotalUsers:          totalUsers,
            ActiveUsers:         activeUsers,
            LockedUsers:         lockedUsers,
            TotalConnections:    totalConnections,
            ConnectionsByStatus: connectionsByStatus,
            TotalItems:          totalItems,
            SyncErrorsLast24h:   syncErrorsLast24h);
    }
}
