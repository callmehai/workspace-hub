using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Infrastructure.Repositories;

/// <summary>Repository User — thêm truy vấn theo email + đếm cho health-check.</summary>
public class UserRepository : GenericRepository<User>, IUserRepository
{
    public UserRepository(AppDbContext db) : base(db) { }

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
        => Set.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
        => Set.AnyAsync(u => u.Email == email, ct);

    public Task<int> CountAsync(CancellationToken ct = default)
        => Set.CountAsync(ct);

    public Task<User?> GetByGoogleSubAsync(string googleSub, CancellationToken ct = default)
        => Set.FirstOrDefaultAsync(u => u.GoogleSub == googleSub, ct);

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<User> Users, int TotalCount)> GetPagedAdminAsync(
        string? search,
        int page,
        int limit,
        CancellationToken ct = default)
    {
        // IQueryable — chưa execute, EF sẽ build 1 SQL duy nhất
        IQueryable<User> query = Set.AsNoTracking();

        // Case-insensitive search: SQL Server CI collation xử lý, KHÔNG dùng ToLower()
        // (ToLower phá index; Contains → LIKE '%...%' trên CI collation là đủ).
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u =>
                u.Email.Contains(term) || u.FullName.Contains(term));
        }

        var totalCount = await query.CountAsync(ct);

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(ct);

        return (users.AsReadOnly(), totalCount);
    }
}

