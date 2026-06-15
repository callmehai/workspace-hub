using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Application.DTOs.Connections;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

[Authorize]
public class ConnectionsController : ApiControllerBase
{
    private readonly IConnectionsService _connections;
    private readonly WorkspaceHub.Application.Interfaces.Repositories.IConnectionRepository _connectionRepository;
    private readonly WorkspaceHub.Application.Abstractions.IGmailGateway _gmailGateway;
    private readonly WorkspaceHub.Application.Mapping.IGmailItemMapper _mapper;
    private readonly WorkspaceHub.Application.Interfaces.Services.IGmailSyncService _syncService;

    public ConnectionsController(
        IConnectionsService connections,
        WorkspaceHub.Application.Interfaces.Repositories.IConnectionRepository connectionRepository,
        WorkspaceHub.Application.Abstractions.IGmailGateway gmailGateway,
        WorkspaceHub.Application.Mapping.IGmailItemMapper mapper,
        WorkspaceHub.Application.Interfaces.Services.IGmailSyncService syncService)
    {
        _connections = connections;
        _connectionRepository = connectionRepository;
        _gmailGateway = gmailGateway;
        _mapper = mapper;
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
        var conn = await _connectionRepository.GetByIdAsync(id, ct);
        if (conn is null) return NotFound();

        if (conn.UserId != CurrentUserId) return Forbid();

        if (conn.ServiceType != WorkspaceHub.Domain.Enums.ServiceType.Gmail)
            return BadRequest(new { message = "Kết nối này không phải Gmail" });

        var profile = await _gmailGateway.GetProfileAsync(conn, ct);
        return Ok(profile);
    }

    [HttpGet("{id:guid}/gmail-sample")]
    public async Task<IActionResult> GetGmailSample(Guid id, CancellationToken ct)
    {
        var conn = await _connectionRepository.GetByIdAsync(id, ct);
        if (conn is null) return NotFound();

        if (conn.UserId != CurrentUserId) return Forbid();

        if (conn.ServiceType != WorkspaceHub.Domain.Enums.ServiceType.Gmail)
            return BadRequest(new { message = "Kết nối này không phải Gmail" });

        var list = await _gmailGateway.ListMessageIdsAsync(conn, null, 1, ct);
        if (list.MessageIds.Count == 0)
        {
            return Ok(new { message = "Hộp thư trống, không có email để map" });
        }

        var msg = await _gmailGateway.GetMessageAsync(conn, list.MessageIds[0], ct);
        var item = _mapper.ToItem(msg, conn.UserId, conn.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        
        return Ok(item);
    }

    [HttpPost("{id:guid}/sync")]
    public async Task<IActionResult> SyncConnection(Guid id, CancellationToken ct)
    {
        var conn = await _connectionRepository.GetByIdAsync(id, ct);
        if (conn is null) return NotFound();

        if (conn.UserId != CurrentUserId) return Forbid();

        if (conn.ServiceType != WorkspaceHub.Domain.Enums.ServiceType.Gmail)
            return BadRequest(new { message = "Kết nối này không phải Gmail" });

        var result = await _syncService.SyncConnectionAsync(conn, 50, ct);
        return Ok(result);
    }
}
