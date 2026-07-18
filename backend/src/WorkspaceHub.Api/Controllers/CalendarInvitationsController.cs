using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

[Authorize]
public class CalendarInvitationsController : ApiControllerBase
{
    private readonly ICalendarInvitationService _service;

    public CalendarInvitationsController(ICalendarInvitationService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CalendarInvitationResponse>>> GetAll(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
        => Ok(await _service.GetForUserAsync(CurrentUserId, from, to, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CalendarInvitationResponse>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetAsync(CurrentUserId, id, ct));

    [HttpPost("{id:guid}/respond")]
    public async Task<ActionResult<CalendarInvitationResponse>> Respond(
        Guid id,
        [FromBody] RespondCalendarInvitationRequest request,
        CancellationToken ct)
        => Ok(await _service.RespondAsync(CurrentUserId, id, request, ct));
}
