using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Api.Controllers;

/// <summary>
/// Important contacts (SCRUM-60): đánh dấu Email/JiraAccount là liên hệ quan trọng.
/// Item sync về từ contact này tự set IsImportant=true.
/// </summary>
[Authorize]
public class ImportantContactsController : ApiControllerBase
{
    private readonly IImportantContactService _service;
    private readonly IValidator<CreateImportantContactRequest> _validator;

    public ImportantContactsController(
        IImportantContactService service,
        IValidator<CreateImportantContactRequest> validator)
    {
        _service = service;
        _validator = validator;
    }

    /// <summary>GET /api/importantcontacts?type= — list contact của user (lọc theo type nếu có).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ImportantContactResponse>>> Get(
        [FromQuery] ImportantContactType? type, CancellationToken ct)
        => Ok(await _service.GetAsync(CurrentUserId, type, ct));

    /// <summary>POST /api/importantcontacts — tạo contact. 409 nếu trùng (UserId,Type,Identifier).</summary>
    [HttpPost]
    public async Task<ActionResult<ImportantContactResponse>> Create(
        [FromBody] CreateImportantContactRequest request, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(request, ct);
        var created = await _service.CreateAsync(CurrentUserId, request, ct);
        return CreatedAtAction(nameof(Get), new { type = created.Type }, created);
    }

    /// <summary>DELETE /api/importantcontacts/{id} — xoá contact (chỉ owner; 404 nếu không phải của mình).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _service.DeleteAsync(CurrentUserId, id, ct);
        return NoContent();
    }
}
