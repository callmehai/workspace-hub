using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>
/// Endpoint health-check mặc định. Controller mỏng: chỉ gọi service, không chứa business logic.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class HealthController : ControllerBase
{
    private readonly IHealthService _health;

    public HealthController(IHealthService health)
    {
        _health = health;
    }

    /// <summary>GET /api/health — kiểm tra app sống + DB reachable.</summary>
    [HttpGet]
    public async Task<ActionResult<HealthDto>> Get(CancellationToken ct)
        => Ok(await _health.CheckAsync(ct));
}
