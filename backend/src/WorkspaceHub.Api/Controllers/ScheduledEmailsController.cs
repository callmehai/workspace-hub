using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OData.Query;
using WorkspaceHub.Application.DTOs.ScheduledEmails;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>OData: GET /api/ScheduledEmails — $filter/$orderby/$top/$skip/$count.</summary>
[Authorize]
public class ScheduledEmailsController : ODataApiControllerBase
{
    private readonly IScheduledEmailsService _service;

    public ScheduledEmailsController(IScheduledEmailsService service) => _service = service;

    [EnableQuery(MaxTop = 100, PageSize = 100)]
    public IQueryable<ScheduledEmailDto> Get() => _service.GetByUserId(CurrentUserId);
}
