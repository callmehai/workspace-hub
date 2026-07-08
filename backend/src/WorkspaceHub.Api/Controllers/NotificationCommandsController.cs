using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Attributes;
using WorkspaceHub.Application.DTOs.Notifications;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Api.Controllers;

/// <summary>REST: PATCH/POST tại api/notifications. List OData → <see cref="NotificationsController"/> /api/Notifications.</summary>
[Authorize]
[ODataIgnored]
[Route("api/notifications")]
public class NotificationCommandsController : ApiControllerBase
{
    private readonly INotificationService _service;
    private readonly IWebHostEnvironment _env;

    public NotificationCommandsController(INotificationService service, IWebHostEnvironment env)
    {
        _service = service;
        _env = env;
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

        var body = JsonSerializer.Serialize(new
        {
            preview = "A test item was just synced.",
            itemTitle = "Test item",
        });
        var dto = await _service.CreateAndSendAsync(
            CurrentUserId,
            NotificationType.ItemSynced,
            "notifications.devSeed",
            body,
            "/inbox",
            ct);
        return Ok(dto);
    }
#endif
}
