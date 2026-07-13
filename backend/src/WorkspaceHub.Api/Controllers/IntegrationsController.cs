using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Application.DTOs.Connections;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>Catalog integration (read-only) — user biết dịch vụ nào đang bật/tắt trước khi connect.</summary>
[Authorize]
[Route("api/integrations")]
public class IntegrationsController : ApiControllerBase
{
    private readonly IConnectionsService _connectionsService;

    public IntegrationsController(IConnectionsService connectionsService)
    {
        _connectionsService = connectionsService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IntegrationResponse>>> List(CancellationToken ct)
    {
        var result = await _connectionsService.GetIntegrationsAsync(ct);
        return Ok(result);
    }
}
