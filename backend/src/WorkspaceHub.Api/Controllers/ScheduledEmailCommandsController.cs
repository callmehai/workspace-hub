using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Attributes;
using WorkspaceHub.Application.DTOs.ScheduledEmails;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>REST: POST/PATCH/GET-by-id tại api/scheduled-emails. List OData → <see cref="ScheduledEmailsController"/> /api/ScheduledEmails.</summary>
[Authorize]
[ODataIgnored]
[Route("api/scheduled-emails")]
public class ScheduledEmailCommandsController : ApiControllerBase
{
    private readonly IScheduledEmailsService _service;
    private readonly IValidator<CreateScheduledEmailRequest> _validator;

    public ScheduledEmailCommandsController(
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
