using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Application.DTOs.Connections;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

[Authorize]
public class ConnectionsController : ApiControllerBase
{
    private readonly IConnectionsService _connections;
    private readonly IGmailSyncService _syncService;

    public ConnectionsController(
        IConnectionsService connections,
        IGmailSyncService syncService)
    {
        _connections = connections;
        _syncService = syncService;
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

    [HttpGet("{id:guid}/gmail-profile")]
    public async Task<IActionResult> GetGmailProfile(Guid id, CancellationToken ct)
    {
        return Ok(await _syncService.GetProfileAsync(id, CurrentUserId, ct));
    }

    [HttpGet("{id:guid}/gmail-sample")]
    public async Task<IActionResult> GetGmailSample(Guid id, CancellationToken ct)
    {
        return Ok(await _syncService.GetSampleAsync(id, CurrentUserId, ct));
    }

    [HttpPost("{id:guid}/sync")]
    public async Task<IActionResult> SyncConnection(Guid id, CancellationToken ct)
    {
        return Ok(await _syncService.SyncAsync(id, CurrentUserId, 50, ct));
    }
}
