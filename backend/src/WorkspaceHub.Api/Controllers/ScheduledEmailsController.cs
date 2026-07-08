using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Attributes;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.ScheduledEmails;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Enums;
using Microsoft.AspNetCore.OData.Query;
namespace WorkspaceHub.Api.Controllers;

[Authorize]
[ODataIgnored]
[Route("api/scheduled-emails")]
public class ScheduledEmailsController : ApiControllerBase
{
    private readonly IScheduledEmailsService _service;
    private readonly IValidator<CreateScheduledEmailRequest> _validator;

    public ScheduledEmailsController(
        IScheduledEmailsService service,
        IValidator<CreateScheduledEmailRequest> validator)
    {
        _service = service;
        _validator = validator;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateScheduledEmailRequest request, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(request, ct);
        var email = await _service.CreateAsync(CurrentUserId, request, ct);
        return CreatedAtAction(nameof(GetById), new { id = email.Id }, email);
    }

    [HttpGet]
    [EnableQuery]
    public async Task<ActionResult<IEnumerable<ScheduledEmailDto>>> Get(CancellationToken ct = default)
    {
        var items = await _service.GetByUserIdAsync(CurrentUserId, ct);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var email = await _service.GetByIdAsync(CurrentUserId, id, ct);
        return Ok(email);
    }

    [HttpPatch("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var email = await _service.CancelAsync(CurrentUserId, id, ct);
        return Ok(email);
    }
}
