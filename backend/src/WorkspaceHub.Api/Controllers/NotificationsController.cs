using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Attributes;
using WorkspaceHub.Application.DTOs.Notifications;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Api.Controllers;

[Authorize]
[ODataIgnored]
public class NotificationsController : ApiControllerBase
{
    private readonly INotificationService _service;
    private readonly IWebHostEnvironment _env;

    public NotificationsController(INotificationService service, IWebHostEnvironment env)
    {
        _service = service;
        _env = env;
    }

    /// <summary>
    /// GET /api/Notifications — OData $filter/$orderby/$top/$skip/$count (in-memory).
    /// </summary>
    [HttpGet]
    [EnableQuery]
    public async Task<ActionResult<IEnumerable<NotificationDto>>> Get(CancellationToken ct = default)
    {
        var items = await _service.GetByUserIdAsync(CurrentUserId, ct);
        return Ok(items);
    }

    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken ct = default)
    {
        await _service.MarkAsReadAsync(CurrentUserId, id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken ct = default)
    {
        await _service.MarkAllAsReadAsync(CurrentUserId, ct);
        return NoContent();
    }

#if DEBUG
    [HttpPost("dev/seed")]
    public async Task<ActionResult<NotificationDto>> DevSeed(CancellationToken ct = default)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        var dto = await _service.CreateAndSendAsync(
            CurrentUserId,
            NotificationType.ItemSynced,
            "Test sync",
            "Một item mới vừa được sync.",
            "/inbox",
            ct);
        return Ok(dto);
    }
#endif
}
