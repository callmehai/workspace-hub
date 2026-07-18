using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Repositories;

/// <summary>Repository riêng cho User (dùng ở Auth — SCRUM-9/10).</summary>
public interface IUserRepository : IGenericRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetByEmailsAsync(IReadOnlyCollection<string> emails, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<User?> GetByGoogleSubAsync(string googleSub, CancellationToken ct = default);
}

