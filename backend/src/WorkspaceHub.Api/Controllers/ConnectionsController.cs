using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Application.DTOs.Connections;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

[Authorize]
public class ConnectionsController : ApiControllerBase
{
    private readonly IConnectionsService _connections;

    public ConnectionsController(IConnectionsService connections)
    {
        _connections = connections;
    }

    /// <summary>POST /api/connections/oauth/start — build authorization URL.</summary>
    [HttpPost("oauth/start")]
    public async Task<IActionResult> InitiateConnection(
        [FromBody] InitiateConnectionRequest request,
        CancellationToken ct)
    {
        var userId = CurrentUserId;

        var result = await _connections.InitiateConnectionAsync(
            request.IntegrationKey,
            request.ServiceType,
            request.RedirectUri,
            userId,
            ct);

        return Ok(new InitiateConnectionResponse
        {
            AuthorizationUrl = result.AuthorizationUrl,
            State = result.State
        });
    }

    /// <summary>POST /api/connections/oauth/callback — exchange code, lưu Connections (mô hình B).</summary>
    [HttpPost("oauth/callback")]
    public async Task<IActionResult> CompleteConnection(
        [FromBody] CompleteConnectionRequest request,
        CancellationToken ct)
    {
        var userId = CurrentUserId;

        var result = await _connections.CompleteConnectionAsync(request.Code, request.State, userId, ct);

        var response = new CompleteConnectionResponse(
            result.IntegrationKey,
            result.ProviderAccountId,
            result.Connections
                .Select(c => new ConnectionItem(c.Id, c.ServiceType, c.Status))
                .ToList());

        return StatusCode(201, response);
    }

}
