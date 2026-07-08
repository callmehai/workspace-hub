using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>OData: GET /api/EmailContactSuggestions?connectionId= — $filter/$orderby/$top/$skip/$count.</summary>
[Authorize]
public class EmailContactSuggestionsController : ODataApiControllerBase
{
    private readonly ISendEmailService _service;

    public EmailContactSuggestionsController(ISendEmailService service) => _service = service;

    [EnableQuery(MaxTop = 20)]
    public async Task<IActionResult> Get([FromQuery] Guid connectionId, CancellationToken ct = default)
    {
        if (connectionId == Guid.Empty)
            return BadRequest(new { error = "connectionId is required" });

        var query = await _service.QueryContactSuggestionsAsync(CurrentUserId, connectionId, ct);
        return Ok(query);
    }
}
