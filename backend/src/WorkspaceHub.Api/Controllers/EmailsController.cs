using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Attributes;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>REST: send/signature tại api/emails. Contact suggest OData → <see cref="EmailContactSuggestionsController"/>.</summary>
[Authorize]
[ODataIgnored]
[Route("api/emails")]
public class EmailsController : ApiControllerBase
{
    private readonly ISendEmailService _service;
    private readonly IValidator<SendEmailRequest> _validator;
    private readonly IValidator<ReplyEmailRequest> _replyValidator;
    private readonly IValidator<ForwardEmailRequest> _forwardValidator;
    private readonly IValidator<SaveDraftRequest> _draftValidator;

    public EmailsController(
        ISendEmailService service, 
        IValidator<SendEmailRequest> validator,
        IValidator<ReplyEmailRequest> replyValidator,
        IValidator<ForwardEmailRequest> forwardValidator,
        IValidator<SaveDraftRequest> draftValidator)
    {
        _service = service;
        _validator = validator;
        _replyValidator = replyValidator;
        _forwardValidator = forwardValidator;
        _draftValidator = draftValidator;
    }

    // ~40MB: đủ chứa attachment tối đa 25MB (đã decode) khi mã hoá base64 (~33MB) + overhead JSON.
    private const int AttachmentRequestSizeLimit = 40 * 1024 * 1024;

    /// <summary>Gửi email trực tiếp (gửi ngay) qua Gmail. 200 + { messageId, sentAt }.</summary>
    [HttpPost("send")]
    [RequestSizeLimit(AttachmentRequestSizeLimit)]
    public async Task<IActionResult> Send([FromBody] SendEmailRequest request, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(request, ct);
        var result = await _service.SendAsync(CurrentUserId, request, ct);
        return Ok(result);
    }

    /// <summary>Lấy chữ ký Gmail của connection để hiển thị/đính kèm. 200 + { signature } ("" nếu chưa có / thiếu scope).</summary>
    [HttpGet("signature")]
    public async Task<IActionResult> Signature([FromQuery] Guid connectionId, CancellationToken ct)
    {
        var signature = await _service.GetSignatureAsync(CurrentUserId, connectionId, ct);
        return Ok(new { signature = signature ?? "" });
    }


    /// <summary>
    /// Lấy toàn bộ luồng hội thoại của một email (bao gồm cả thư gửi/nhận).
    /// Trả về metadata và HTML body của các tin nhắn trong thread.
    /// </summary>
    [HttpGet("{itemId}/thread")]
    public async Task<IActionResult> GetThread([FromRoute] Guid itemId, CancellationToken ct)
    {
        var result = await _service.GetThreadAsync(CurrentUserId, itemId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Trả lời (Reply / Reply-All) trong một luồng hội thoại email đã có.
    /// Trả về messageId mới của Gmail.
    /// </summary>
    [HttpPost("reply")]
    [RequestSizeLimit(AttachmentRequestSizeLimit)]
    public async Task<IActionResult> Reply([FromBody] ReplyEmailRequest request, CancellationToken ct)
    {
        await _replyValidator.ValidateAndThrowAsync(request, ct);
        var result = await _service.ReplyAsync(CurrentUserId, request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Chuyển tiếp (Forward) một email trong luồng, bao gồm tùy chọn đính kèm file gốc.
    /// Trả về messageId mới của Gmail.
    /// </summary>
    [HttpPost("forward")]
    [RequestSizeLimit(AttachmentRequestSizeLimit)]
    public async Task<IActionResult> Forward([FromBody] ForwardEmailRequest request, CancellationToken ct)
    {
        await _forwardValidator.ValidateAndThrowAsync(request, ct);
        var result = await _service.ForwardAsync(CurrentUserId, request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Tải một file đính kèm trực tiếp từ Gmail thông qua Backend.
    /// Trả về file stream để tải về.
    /// </summary>
    [HttpGet("{itemId}/messages/{messageId}/attachments/{attachmentId}")]
    public async Task<IActionResult> GetAttachment(
        [FromRoute] Guid itemId,
        [FromRoute] string messageId,
        [FromRoute] string attachmentId,
        [FromQuery] string? filename,
        [FromQuery] string? mimeType,
        CancellationToken ct)
    {
        var attachment = await _service.GetAttachmentAsync(CurrentUserId, itemId, messageId, attachmentId, filename, mimeType, ct);
        return File(attachment.Data, attachment.MimeType, attachment.Filename);
    }

    /// <summary>
    /// Tải toàn bộ attachment của 1 message (trong thread) dưới dạng 1 file .zip.
    /// </summary>
    [HttpGet("{itemId}/messages/{messageId}/attachments/zip")]
    public async Task<IActionResult> DownloadAllAttachments([FromRoute] Guid itemId, [FromRoute] string messageId, CancellationToken ct)
    {
        var zip = await _service.GetAttachmentsZipAsync(CurrentUserId, itemId, messageId, ct);
        return File(zip, "application/zip", "attachments.zip");
    }

    /// <summary>Tạo nháp mới. 200 + ItemResponse.</summary>
    [HttpPost("drafts")]
    [RequestSizeLimit(AttachmentRequestSizeLimit)]
    public async Task<IActionResult> CreateDraft([FromBody] SaveDraftRequest request, CancellationToken ct)
    {
        await _draftValidator.ValidateAndThrowAsync(request, ct);
        var result = await _service.SaveDraftAsync(CurrentUserId, request, null, ct);
        return Ok(result);
    }

    /// <summary>Cập nhật nháp đã có. 200 + ItemResponse.</summary>
    [HttpPut("drafts/{itemId:guid}")]
    [RequestSizeLimit(AttachmentRequestSizeLimit)]
    public async Task<IActionResult> UpdateDraft([FromRoute] Guid itemId, [FromBody] SaveDraftRequest request, CancellationToken ct)
    {
        await _draftValidator.ValidateAndThrowAsync(request, ct);
        var result = await _service.SaveDraftAsync(CurrentUserId, request, itemId, ct);
        return Ok(result);
    }

    /// <summary>Gửi nháp đã có. 200 + SendEmailResult.</summary>
    [HttpPost("drafts/{itemId:guid}/send")]
    public async Task<IActionResult> SendDraft([FromRoute] Guid itemId, CancellationToken ct)
    {
        var result = await _service.SendDraftAsync(CurrentUserId, itemId, ct);
        return Ok(result);
    }

    /// <summary>Xoá nháp (chuyển message nháp vào thùng rác trên Gmail và xoá item local). 200 OK.</summary>
    [HttpDelete("drafts/{itemId:guid}")]
    public async Task<IActionResult> DiscardDraft([FromRoute] Guid itemId, CancellationToken ct)
    {
        await _service.DiscardDraftAsync(CurrentUserId, itemId, ct);
        return NoContent();
    }
}
