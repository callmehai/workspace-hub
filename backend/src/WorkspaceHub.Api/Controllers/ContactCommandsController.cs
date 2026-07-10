using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Attributes;
using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>REST: GET/POST/PATCH/DELETE tại api/contacts. List OData → <see cref="ContactsController"/> /api/Contacts.</summary>
[Authorize]
[ODataIgnored]
[Route("api/contacts")]
public class ContactCommandsController : ApiControllerBase
{
    private readonly IGoogleContactService _service;
    private readonly IValidator<CreateContactRequest> _createValidator;
    private readonly IValidator<PatchContactRequest> _patchValidator;

    public ContactCommandsController(
        IGoogleContactService service,
        IValidator<CreateContactRequest> createValidator,
        IValidator<PatchContactRequest> patchValidator)
    {
        _service = service;
        _createValidator = createValidator;
        _patchValidator = patchValidator;
    }

    [HttpGet("suggest")]
    public async Task<ActionResult<IReadOnlyList<ContactSuggestionDto>>> Suggest(
        [FromQuery] Guid connectionId,
        [FromQuery] string query,
        [FromQuery] int limit = 10,
        CancellationToken ct = default) =>
        Ok(await _service.SuggestAsync(CurrentUserId, connectionId, query, limit, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContactDetailDto>> GetById(Guid id, CancellationToken ct = default) =>
        Ok(await _service.GetByIdAsync(CurrentUserId, id, ct));

    [HttpPost]
    public async Task<ActionResult<ContactDto>> Create(
        [FromBody] CreateContactRequest request,
        CancellationToken ct = default)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var created = await _service.CreateAsync(CurrentUserId, request, ct);
        return Created($"/api/contacts/{created.Id}", created);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ContactDetailDto>> Patch(
        Guid id,
        [FromBody] PatchContactRequest request,
        CancellationToken ct = default)
    {
        await _patchValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.UpdateAsync(CurrentUserId, id, request, ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        await _service.DeleteAsync(CurrentUserId, id, ct);
        return NoContent();
    }
}
