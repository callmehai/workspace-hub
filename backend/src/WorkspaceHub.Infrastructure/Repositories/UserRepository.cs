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
}
