using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Attributes;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>
/// Items listing endpoint (Week 5 — search/filter/pagination).
/// Controller mỏng: nhận request → validate → gọi service → trả kết quả.
/// KHÔNG chứa business logic (xem CONVENTIONS.md).
/// </summary>
[Authorize]
[ODataIgnored]
public class ItemsController : ApiControllerBase
{
    private readonly IItemService _itemService;
    private readonly IItemWriteBackService _writeBackService;
    private readonly IValidator<GetItemsRequest> _validator;
    private readonly IValidator<UpdateItemStatusRequest> _updateStatusValidator;
    private readonly IValidator<CreateNoteRequest> _createNoteValidator;
    private readonly IValidator<CreateEventRequest> _createEventValidator;
    private readonly IValidator<CreateTicketRequest> _createTicketValidator;
    private readonly IValidator<PatchItemRequest> _patchItemValidator;

    public ItemsController(
        IItemService itemService,
        IItemWriteBackService writeBackService,
        IValidator<GetItemsRequest> validator,
        IValidator<UpdateItemStatusRequest> updateStatusValidator,
        IValidator<CreateNoteRequest> createNoteValidator,
        IValidator<CreateEventRequest> createEventValidator,
        IValidator<CreateTicketRequest> createTicketValidator,
        IValidator<PatchItemRequest> patchItemValidator)
    {
        _itemService = itemService;
        _writeBackService = writeBackService;
        _validator = validator;
        _updateStatusValidator = updateStatusValidator;
        _createNoteValidator = createNoteValidator;
        _createEventValidator = createEventValidator;
        _createTicketValidator = createTicketValidator;
        _patchItemValidator = patchItemValidator;
    }

    /// <summary>
    /// GET /api/items?folderId=&amp;status=&amp;type=&amp;isImportant=&amp;search=&amp;page=&amp;limit=
    /// Trả envelope { items, total, page, limit } theo API.md.
    /// Chỉ trả items thuộc authenticated user.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ItemResponse>>> GetItems(
        [FromQuery] GetItemsRequest request,
        CancellationToken ct = default)
    {
        // Validate qua FluentValidation — ValidationException được ExceptionMiddleware format thành 400 chuẩn.
        await _validator.ValidateAndThrowAsync(request, ct);

        var result = await _itemService.GetItemsAsync(CurrentUserId, request, ct);
        return Ok(result);
    }

    /// <summary>
    /// PATCH /api/items/{id}/status — đổi trạng thái Kanban.
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ItemResponse>> UpdateStatus(
        Guid id,
        [FromBody] UpdateItemStatusRequest request,
        CancellationToken ct = default)
    {
        await _updateStatusValidator.ValidateAndThrowAsync(request, ct);
        var updated = await _itemService.UpdateStatusAsync(CurrentUserId, id, request, ct);
        return Ok(updated);
    }

    /// <summary>
    /// GET /api/items/{id} — lấy chi tiết một item.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ItemResponse>> GetItemById(
        Guid id,
        CancellationToken ct = default)
    {
        var item = await _itemService.GetItemByIdAsync(CurrentUserId, id, ct);
        return Ok(item);
    }

    /// <summary>
    /// POST /api/items/note — tạo ghi chú nội bộ (Note) mới.
    /// </summary>
    [HttpPost("note")]
    public async Task<ActionResult<ItemResponse>> CreateNote(
        [FromBody] CreateNoteRequest request,
        CancellationToken ct = default)
    {
        await _createNoteValidator.ValidateAndThrowAsync(request, ct);
        var created = await _itemService.CreateNoteAsync(CurrentUserId, request, ct);
        // Trả 201 trỏ về GetItemById
        return CreatedAtAction(nameof(GetItemById), new { id = created.Id }, created);
    }

    /// <summary>
    /// PATCH /api/items/{id} — cập nhật writeback item.
    /// </summary>
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ItemResponse>> PatchItem(
        Guid id,
        [FromBody] PatchItemRequest request,
        CancellationToken ct = default)
    {
        await _patchItemValidator.ValidateAndThrowAsync(request, ct);
        var updated = await _writeBackService.PatchItemAsync(id, CurrentUserId, request, ct);
        return Ok(updated);
    }

    /// <summary>
    /// POST /api/items/event — tạo event mới trên Google Calendar.
    /// </summary>
    [HttpPost("event")]
    public async Task<ActionResult<ItemResponse>> CreateEvent(
        [FromBody] CreateEventRequest request,
        CancellationToken ct = default)
    {
        await _createEventValidator.ValidateAndThrowAsync(request, ct);
        var created = await _writeBackService.CreateEventAsync(CurrentUserId, request, ct);
        return CreatedAtAction(nameof(GetItemById), new { id = created.Id }, created);
    }

    /// <summary>
    /// POST /api/items/ticket — tạo issue Jira mới (SCRUM-56).
    /// </summary>
    [HttpPost("ticket")]
    public async Task<ActionResult<ItemResponse>> CreateTicket(
        [FromBody] CreateTicketRequest request,
        CancellationToken ct = default)
    {
        await _createTicketValidator.ValidateAndThrowAsync(request, ct);
        var created = await _writeBackService.CreateTicketAsync(CurrentUserId, request, ct);
        return CreatedAtAction(nameof(GetItemById), new { id = created.Id }, created);
    }

    /// <summary>
    /// DELETE /api/items/{id} — xoá writeback item.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteItem(
        Guid id,
        CancellationToken ct = default)
    {
        await _writeBackService.DeleteItemAsync(id, CurrentUserId, ct);
        return NoContent();
    }
}
