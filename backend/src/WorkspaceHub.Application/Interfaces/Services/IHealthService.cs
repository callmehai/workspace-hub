using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>Business logic cho health-check (kiểm tra DB reachable).</summary>
public interface IHealthService
{
    Task<HealthDto> CheckAsync(CancellationToken ct = default);
}
