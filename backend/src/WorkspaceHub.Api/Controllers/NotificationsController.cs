using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OData.Query;
using WorkspaceHub.Application.DTOs.Notifications;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>OData: GET /api/Notifications — $filter/$orderby/$top/$skip/$count.</summary>
[Authorize]
public class NotificationsController : ODataApiControllerBase
{
    private readonly INotificationService _service;

    public NotificationsController(INotificationService service) => _service = service;

    [EnableQuery(MaxTop = 100, PageSize = 100)]
    public IQueryable<NotificationDto> Get() => _service.GetByUserId(CurrentUserId);
}
