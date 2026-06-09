using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Application.Services;

/// <summary>
/// Health-check tầng business: hỏi repository xem DB có reachable không.
/// Minh hoạ luồng Controller → Service → Repository → DbContext.
/// </summary>
public class HealthService : IHealthService
{
    private readonly IUserRepository _users;

    public HealthService(IUserRepository users)
    {
        _users = users;
    }

    public async Task<HealthDto> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var count = await _users.CountAsync(ct);
            return new HealthDto("Healthy", "Connected", count, DateTime.UtcNow);
        }
        catch
        {
            // DB chưa migrate / không reachable → vẫn trả 200 nhưng báo Degraded.
            return new HealthDto("Degraded", "Unreachable", 0, DateTime.UtcNow);
        }
    }
}
