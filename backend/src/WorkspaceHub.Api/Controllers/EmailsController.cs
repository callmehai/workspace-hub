using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Attributes;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

[Authorize]
[ODataIgnored]
[Route("api/emails")]
public class EmailsController : ApiControllerBase
{
    private readonly ISendEmailService _service;
    private readonly IValidator<SendEmailRequest> _validator;

    public EmailsController(ISendEmailService service, IValidator<SendEmailRequest> validator)
    {
        _service = service;
        _validator = validator;
    }

    /// <summary>Gửi email trực tiếp (gửi ngay) qua Gmail. 200 + { messageId, sentAt }.</summary>
    [HttpPost("send")]
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
    /// GET /api/emails/contacts/suggest?connectionId= — cache GoogleContacts (Contact + OtherContact).
    /// OData in-memory: $filter, $orderby, $top, $skip, $count, $select.
    /// </summary>
    [HttpGet("contacts/suggest")]
    [EnableQuery(MaxTop = 20)]
    public async Task<ActionResult<IEnumerable<ContactSuggestionDto>>> SuggestContacts(
        [FromQuery] Guid connectionId,
        CancellationToken ct = default)
    {
        var items = await _service.GetContactSuggestionsAsync(CurrentUserId, connectionId, ct);
        return Ok(items);
    }
}
