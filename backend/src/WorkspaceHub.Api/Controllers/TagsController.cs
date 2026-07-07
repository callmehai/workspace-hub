using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Attributes;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>
/// Tag CRUD + gắn/gỡ tag khỏi item (SCRUM-70).
/// Tag là label private của user (không share). Controller mỏng: validate → gọi service → trả kết quả.
/// </summary>
[Authorize]
[ODataIgnored]
public class TagsController : ApiControllerBase
{
    private readonly ITagService _tagService;
    private readonly IValidator<CreateTagRequest> _createValidator;
    private readonly IValidator<UpdateTagRequest> _updateValidator;
    private readonly IValidator<AssignTagRequest> _assignValidator;

    public TagsController(
        ITagService tagService,
        IValidator<CreateTagRequest> createValidator,
        IValidator<UpdateTagRequest> updateValidator,
        IValidator<AssignTagRequest> assignValidator)
    {
        _tagService = tagService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _assignValidator = assignValidator;
    }

    /// <summary>GET /api/tags — list tag của user (kèm ItemCount). Hỗ trợ OData $filter/$orderby/$top/$skip/$select.</summary>
    [HttpGet]
    [EnableQuery]
    public async Task<ActionResult<IEnumerable<TagResponse>>> GetTags(CancellationToken ct = default)
        => Ok(await _tagService.GetAsync(CurrentUserId, ct));

    /// <summary>POST /api/tags — tạo tag. 409 nếu trùng tên trong phạm vi user.</summary>
    [HttpPost]
    public async Task<ActionResult<TagResponse>> CreateTag(
        [FromBody] CreateTagRequest request, CancellationToken ct = default)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var created = await _tagService.CreateAsync(CurrentUserId, request, ct);
        return CreatedAtAction(nameof(GetTags), null, created);
    }

    /// <summary>PUT /api/tags/{id} — đổi tên/màu tag (owner-only, 404 nếu không phải của mình; 409 trùng tên).</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TagResponse>> UpdateTag(
        Guid id, [FromBody] UpdateTagRequest request, CancellationToken ct = default)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);
        var updated = await _tagService.UpdateAsync(CurrentUserId, id, request, ct);
        return Ok(updated);
    }

    /// <summary>DELETE /api/tags/{id} — xoá tag (hard delete; cascade gỡ mọi TagAssignment, Item giữ nguyên).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteTag(Guid id, CancellationToken ct = default)
    {
        await _tagService.DeleteAsync(CurrentUserId, id, ct);
        return NoContent();
    }

    /// <summary>POST /api/tags/{id}/items — gắn tag vào item. 409 nếu đã gắn; 404 nếu tag/item không thuộc user.</summary>
    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<TagAssignmentResponse>> AssignTag(
        Guid id, [FromBody] AssignTagRequest request, CancellationToken ct = default)
    {
        await _assignValidator.ValidateAndThrowAsync(request, ct);
        var result = await _tagService.AssignAsync(CurrentUserId, id, request, ct);
        return StatusCode(201, result);
    }

    /// <summary>DELETE /api/tags/{id}/items/{itemId} — gỡ tag khỏi item. 404 nếu chưa gắn.</summary>
    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> UnassignTag(Guid id, Guid itemId, CancellationToken ct = default)
    {
        await _tagService.UnassignAsync(CurrentUserId, id, itemId, ct);
        return NoContent();
    }
}
