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
    private readonly IJiraTicketService _ticketService;
    private readonly IValidator<GetItemsRequest> _validator;
    private readonly IValidator<UpdateItemStatusRequest> _updateStatusValidator;
    private readonly IValidator<CreateNoteRequest> _createNoteValidator;
    private readonly IValidator<CreateEventRequest> _createEventValidator;
    private readonly IValidator<CreateTicketRequest> _createTicketValidator;
    private readonly IValidator<PatchItemRequest> _patchItemValidator;
    private readonly IValidator<RsvpRequest> _rsvpValidator;
    private readonly IValidator<SendEmailToGuestsRequest> _sendEmailValidator;

    public ItemsController(
        IItemService itemService,
        IItemWriteBackService writeBackService,
        IJiraTicketService ticketService,
        IValidator<GetItemsRequest> validator,
        IValidator<UpdateItemStatusRequest> updateStatusValidator,
        IValidator<CreateNoteRequest> createNoteValidator,
        IValidator<CreateEventRequest> createEventValidator,
        IValidator<CreateTicketRequest> createTicketValidator,
        IValidator<PatchItemRequest> patchItemValidator,
        IValidator<RsvpRequest> rsvpValidator,
        IValidator<SendEmailToGuestsRequest> sendEmailValidator)
    {
        _itemService = itemService;
        _writeBackService = writeBackService;
        _ticketService = ticketService;
        _validator = validator;
        _updateStatusValidator = updateStatusValidator;
        _createNoteValidator = createNoteValidator;
        _createEventValidator = createEventValidator;
        _createTicketValidator = createTicketValidator;
        _patchItemValidator = patchItemValidator;
        _rsvpValidator = rsvpValidator;
        _sendEmailValidator = sendEmailValidator;
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
    /// GET /api/items/assignees — danh sách người phụ trách (assignee) suy từ ticket Jira đã sync,
    /// dùng cho filter theo user ở tab Jira. Gồm "unassigned" nếu có ticket chưa gán.
    /// </summary>
    [HttpGet("assignees")]
    public async Task<ActionResult<IReadOnlyList<JiraAssigneeDto>>> GetAssignees(CancellationToken ct = default)
    {
        var result = await _itemService.GetTicketAssigneesAsync(CurrentUserId, ct);
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
    /// GET /api/items/{id}/calendar-details — lấy chi tiết sự kiện lịch từ Google Calendar kèm nhắc nhở.
    /// </summary>
    [HttpGet("{id:guid}/calendar-details")]
    public async Task<ActionResult<CalendarEventDetailResponse>> GetCalendarEventDetail(
        Guid id,
        CancellationToken ct = default)
    {
        var detail = await _itemService.GetCalendarEventDetailAsync(CurrentUserId, id, ct);
        return Ok(detail);
    }

    /// <summary>
    /// PATCH /api/items/{id}/rsvp — cập nhật RSVP cho sự kiện.
    /// </summary>
    [HttpPatch("{id:guid}/rsvp")]
    public async Task<IActionResult> RsvpEvent(
        Guid id,
        [FromBody] RsvpRequest request,
        CancellationToken ct = default)
    {
        await _rsvpValidator.ValidateAndThrowAsync(request, ct);
        await _itemService.RsvpEventAsync(CurrentUserId, id, request, ct);
        return NoContent();
    }

    /// <summary>
    /// POST /api/items/{id}/send-email-guests — gửi email mời khách hoặc nội dung chi tiết sự kiện.
    /// </summary>
    [HttpPost("{id:guid}/send-email-guests")]
    public async Task<IActionResult> SendEmailToGuests(
        Guid id,
        [FromBody] SendEmailToGuestsRequest request,
        CancellationToken ct = default)
    {
        await _sendEmailValidator.ValidateAndThrowAsync(request, ct);
        await _itemService.SendEmailToGuestsAsync(CurrentUserId, id, request, ct);
        return NoContent();
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

    /// <summary>
    /// PATCH /api/items/{id}/important — đánh dấu quan trọng (sao) nội bộ.
    /// </summary>
    [HttpPatch("{id:guid}/important")]
    public async Task<ActionResult<ItemResponse>> UpdateImportant(
        Guid id,
        [FromBody] UpdateItemImportantRequest request,
        CancellationToken ct = default)
    {
        var updated = await _itemService.ToggleImportantAsync(CurrentUserId, id, request.IsImportant, ct);
        return Ok(updated);
    }

    // ───────────────────── Jira comment (2 chiều) ─────────────────────

    /// <summary>GET /api/items/{id}/comments — list comment của ticket Jira.</summary>
    [HttpGet("{id:guid}/comments")]
    public async Task<ActionResult<IReadOnlyList<JiraCommentDto>>> GetComments(Guid id, CancellationToken ct = default)
        => Ok(await _ticketService.GetCommentsAsync(id, CurrentUserId, ct));

    /// <summary>POST /api/items/{id}/comments — thêm comment.</summary>
    [HttpPost("{id:guid}/comments")]
    public async Task<ActionResult<JiraCommentDto>> AddComment(Guid id, [FromBody] TicketCommentBody body, CancellationToken ct = default)
        => Ok(await _ticketService.AddCommentAsync(id, CurrentUserId, body.Body, body.MediaIds, ct));

    /// <summary>PUT /api/items/{id}/comments/{commentId} — sửa comment.</summary>
    [HttpPut("{id:guid}/comments/{commentId}")]
    public async Task<ActionResult<JiraCommentDto>> UpdateComment(Guid id, string commentId, [FromBody] TicketCommentBody body, CancellationToken ct = default)
        => Ok(await _ticketService.UpdateCommentAsync(id, CurrentUserId, commentId, body.Body, ct));

    /// <summary>DELETE /api/items/{id}/comments/{commentId} — xoá comment.</summary>
    [HttpDelete("{id:guid}/comments/{commentId}")]
    public async Task<IActionResult> DeleteComment(Guid id, string commentId, CancellationToken ct = default)
    {
        await _ticketService.DeleteCommentAsync(id, CurrentUserId, commentId, ct);
        return NoContent();
    }

    // ───────────────────── Jira attachment (2 chiều) ─────────────────────

    /// <summary>GET /api/items/{id}/attachments — list attachment metadata.</summary>
    [HttpGet("{id:guid}/attachments")]
    public async Task<ActionResult<IReadOnlyList<JiraAttachmentDto>>> GetAttachments(Guid id, CancellationToken ct = default)
        => Ok(await _ticketService.GetAttachmentsAsync(id, CurrentUserId, ct));

    /// <summary>GET /api/items/{id}/attachments/{attId}/download — tải file.</summary>
    [HttpGet("{id:guid}/attachments/{attId}/download")]
    public async Task<IActionResult> DownloadAttachment(Guid id, string attId, CancellationToken ct = default)
    {
        var (data, mime, filename) = await _ticketService.DownloadAttachmentAsync(id, CurrentUserId, attId, ct);
        return File(data, mime, filename);
    }

    /// <summary>POST /api/items/{id}/attachments — upload file (multipart form-data, field "file").</summary>
    [HttpPost("{id:guid}/attachments")]
    [RequestSizeLimit(30_000_000)] // ~30MB
    public async Task<ActionResult<IReadOnlyList<JiraAttachmentDto>>> UploadAttachment(Guid id, IFormFile file, CancellationToken ct = default)
    {
        if (file == null || file.Length == 0) return BadRequest("Thiếu file.");
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var result = await _ticketService.UploadAttachmentAsync(
            id, CurrentUserId, file.FileName, file.ContentType ?? "application/octet-stream", ms.ToArray(), ct);
        return Ok(result);
    }

    /// <summary>DELETE /api/items/{id}/attachments/{attId} — xoá attachment.</summary>
    [HttpDelete("{id:guid}/attachments/{attId}")]
    public async Task<IActionResult> DeleteAttachment(Guid id, string attId, CancellationToken ct = default)
    {
        await _ticketService.DeleteAttachmentAsync(id, CurrentUserId, attId, ct);
        return NoContent();
    }
}
