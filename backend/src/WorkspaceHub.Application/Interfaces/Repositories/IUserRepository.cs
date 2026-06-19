using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Repositories;

/// <summary>Repository riêng cho User (dùng ở Auth — SCRUM-9/10).</summary>
public interface IUserRepository : IGenericRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<User?> GetByGoogleSubAsync(string googleSub, CancellationToken ct = default);

    /// <summary>
    /// Paginated user list cho admin dashboard.
    /// Search khớp Email HOẶC FullName (case-insensitive, SQL Server collation).
    /// NOTE: ConnectionCount/ItemCount được project trực tiếp trong AdminService qua AppDbContext
    /// để tránh leak EF projection phức tạp vào repository interface.
    /// </summary>
    Task<(IReadOnlyList<User> Users, int TotalCount)> GetPagedAdminAsync(
        string? search,
        int page,
        int limit,
        CancellationToken ct = default);
}
