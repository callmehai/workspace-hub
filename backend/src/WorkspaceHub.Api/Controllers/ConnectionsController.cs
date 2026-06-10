using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Application.DTOs.Connections;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>
/// Quản lý kết nối OAuth (Google).
/// TODO: Thêm [Authorize] khi JWT được implement (SCRUM-9).
/// </summary>
[ApiController]
[Route("api/connections")]
public class ConnectionsController : ControllerBase
{
    private readonly IConnectionsService _connections;

    public ConnectionsController(IConnectionsService connections)
    {
        _connections = connections;
    }

    /// <summary>
    /// POST /api/connections/oauth/start
    /// Trả về Google authorization URL + state CSRF để FE redirect sang Google.
    /// userId tạm hardcode Guid.Empty — sẽ đọc từ JWT claim "sub" sau SCRUM-9.
    /// </summary>
    [HttpPost("oauth/start")]
    public async Task<IActionResult> InitiateConnection(
        [FromBody] InitiateConnectionRequest request,
        CancellationToken ct)
    {
        // TODO: thay bằng Guid.Parse(User.FindFirst("sub")!.Value) sau khi JWT sẵn sàng.
        var userId = Guid.Empty;

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
    /// PUT /api/connections/{key}/credentials
    /// Encrypt clientId + clientSecret rồi lưu DB.
    /// TODO: thêm [Authorize(Policy="AdminOnly")] sau khi JWT xong (SCRUM-9).
    /// </summary>
    [HttpPut("{key}/credentials")]
    public async Task<IActionResult> SetCredentials(
        string key,
        [FromBody] SetCredentialsRequest request,
        CancellationToken ct)
    {
        await _connections.SetCredentialsAsync(key, request.ClientId, request.ClientSecret, ct);
        return NoContent();
    }
}
