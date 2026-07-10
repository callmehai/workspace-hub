using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>
/// Jira metadata helpers (SCRUM-59) — phục vụ FE render dropdown khi tạo/sửa ticket.
/// Trả dữ liệu live từ Jira (KHÔNG bật OData). Mọi endpoint cần ?connectionId= (ServiceType=Jira).
/// </summary>
[Authorize]
public class JiraController : ApiControllerBase
{
    private readonly IJiraMetadataService _metadata;

    public JiraController(IJiraMetadataService metadata)
    {
        _metadata = metadata;
    }

    /// <summary>GET /api/jira/projects?connectionId= — list project.</summary>
    [HttpGet("projects")]
    public async Task<IActionResult> GetProjects([FromQuery] Guid connectionId, CancellationToken ct)
        => Ok(await _metadata.GetProjectsAsync(connectionId, CurrentUserId, ct));

    /// <summary>GET /api/jira/issue-types?connectionId=&amp;projectKey= — issue type của project.</summary>
    [HttpGet("issue-types")]
    public async Task<IActionResult> GetIssueTypes([FromQuery] Guid connectionId, [FromQuery] string projectKey, CancellationToken ct)
        => Ok(await _metadata.GetIssueTypesAsync(connectionId, CurrentUserId, projectKey, ct));

    /// <summary>GET /api/jira/priorities?connectionId= — danh sách priority.</summary>
    [HttpGet("priorities")]
    public async Task<IActionResult> GetPriorities([FromQuery] Guid connectionId, CancellationToken ct)
        => Ok(await _metadata.GetPrioritiesAsync(connectionId, CurrentUserId, ct));

    /// <summary>GET /api/jira/assignable-users?connectionId=&amp;projectKey=&amp;query= — user gán được.</summary>
    [HttpGet("assignable-users")]
    public async Task<IActionResult> GetAssignableUsers(
        [FromQuery] Guid connectionId, [FromQuery] string projectKey, [FromQuery] string? query, CancellationToken ct)
        => Ok(await _metadata.GetAssignableUsersAsync(connectionId, CurrentUserId, projectKey, query, ct));

    /// <summary>GET /api/jira/transitions?connectionId=&amp;itemId= — transition khả dụng của issue.</summary>
    [HttpGet("transitions")]
    public async Task<IActionResult> GetTransitions([FromQuery] Guid connectionId, [FromQuery] Guid itemId, CancellationToken ct)
        => Ok(await _metadata.GetTransitionsAsync(connectionId, CurrentUserId, itemId, ct));

    /// <summary>GET /api/jira/site?connectionId= — tên + URL Jira site (cho FE hiển thị tên account). 204 nếu không lấy được.</summary>
    [HttpGet("site")]
    public async Task<IActionResult> GetSite([FromQuery] Guid connectionId, CancellationToken ct)
    {
        var site = await _metadata.GetSiteAsync(connectionId, CurrentUserId, ct);
        return site is null ? NoContent() : Ok(site);
    }
}
