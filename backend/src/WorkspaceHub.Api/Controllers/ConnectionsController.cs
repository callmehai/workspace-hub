using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Application.DTOs.Connections;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

[Route("api/connections")]
[Authorize]
public class ConnectionsController : ApiControllerBase
{
    private readonly IConnectionsService _connections;

    public ConnectionsController(IConnectionsService connections)
    {
        _connections = connections;
    }

    /// <summary>POST /api/connections/oauth/start — build Google authorization URL.</summary>
    [HttpPost("oauth/start")]
    public async Task<IActionResult> InitiateConnection(
        [FromBody] InitiateConnectionRequest request,
        CancellationToken ct)
    {
        var userId = CurrentUserId;

        var result = await _connections.InitiateConnectionAsync(
            request.IntegrationKey,
            request.RedirectUri,
            userId,
            ct);

        return Ok(new InitiateConnectionResponse
        {
            AuthorizationUrl = result.AuthorizationUrl,
            State = result.State
        });
    }

    /// <summary>POST /api/connections/oauth/callback — exchange code, lưu OAuthConnection.</summary>
    [HttpPost("oauth/callback")]
    public async Task<IActionResult> CompleteConnection(
        [FromBody] CompleteConnectionRequest request,
        CancellationToken ct)
    {
        var userId = CurrentUserId;

        var result = await _connections.CompleteConnectionAsync(request.Code, request.State, userId, ct);

        var response = new CompleteConnectionResponse(
            result.Id,
            result.IntegrationKey,
            result.ProviderAccountId,
            result.Scopes,
            result.Status,
            result.Services
                .Select(s => new ServiceConnectionItem(s.Id, s.ServiceType, s.IsEnabled))
                .ToList());

        return StatusCode(201, response);
    }

    /// <summary>PUT /api/connections/{key}/credentials — Admin: encrypt + lưu OAuth credentials.</summary>
    [HttpPut("{key}/credentials")]
    [AllowAnonymous]
    public async Task<IActionResult> SetCredentials(
        string key,
        [FromBody] SetCredentialsRequest request,
        CancellationToken ct)
    {
        await _connections.SetCredentialsAsync(key, request.ClientId, request.ClientSecret, ct);
        return NoContent();
    }
}
