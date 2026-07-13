using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Application.DTOs.Connections;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

[Authorize(Roles = "Admin")]
[Route("api/admin/integrations")]
public class AdminIntegrationsController : ApiControllerBase
{
    private readonly IConnectionsService _connectionsService;
    private readonly IValidator<ToggleIntegrationRequest> _validator;

    public AdminIntegrationsController(
        IConnectionsService connectionsService,
        IValidator<ToggleIntegrationRequest> validator)
    {
        _connectionsService = connectionsService;
        _validator = validator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IntegrationResponse>>> List(CancellationToken ct)
    {
        var result = await _connectionsService.GetIntegrationsAsync(ct);
        return Ok(result);
    }

    [HttpPatch("{key}/enable")]
    public async Task<ActionResult<IntegrationResponse>> Toggle(
        string key,
        [FromBody] ToggleIntegrationRequest request,
        CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(request, ct);
        var result = await _connectionsService.ToggleIntegrationAsync(key, request.IsEnabled, ct);
        return Ok(result);
    }
}
