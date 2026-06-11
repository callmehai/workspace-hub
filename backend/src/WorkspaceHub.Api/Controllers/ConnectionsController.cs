using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Application.DTOs.Connections;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>
/// Quản lý kết nối OAuth (Google).
/// Đã tích hợp JWT Authorize qua ApiControllerBase (SCRUM-9).
/// </summary>
[Authorize]
[Route("api/connections")]
public class ConnectionsController : ApiControllerBase
{
    private readonly IConnectionsService _connections;

    public ConnectionsController(IConnectionsService connections)
    {
        _connections = connections;
    }

    /// <summary>
    /// POST /api/connections/oauth/start
    /// Trả về Google authorization URL + state CSRF để FE redirect sang Google.
    /// Lấy userId từ JWT claim "sub".
    /// </summary>
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

    /// <summary>
    /// POST /api/connections/oauth/callback
    /// Nhận code + state từ Google redirect, exchange token, lưu OAuthConnection + ServiceConnections.
    /// Lấy userId từ JWT claim "sub".
    /// </summary>
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

    /// <summary>
    /// PUT /api/connections/{key}/credentials
    /// Encrypt clientId + clientSecret rồi lưu DB.
    /// Chỉ cho phép Admin.
    /// </summary>
    [HttpPut("{key}/credentials")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetCredentials(
        string key,
        [FromBody] SetCredentialsRequest request,
        CancellationToken ct)
    {
        await _connections.SetCredentialsAsync(key, request.ClientId, request.ClientSecret, ct);
        return NoContent();
    }
}
