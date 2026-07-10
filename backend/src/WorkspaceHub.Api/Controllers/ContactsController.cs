using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>OData: GET /api/Contacts?connectionId= — $filter/$orderby/$top/$skip/$count/$select.</summary>
[Authorize]
public class ContactsController : ODataApiControllerBase
{
    private readonly IGoogleContactService _service;

    public ContactsController(IGoogleContactService service) => _service = service;

    [EnableQuery(MaxTop = 100, PageSize = 100)]
    public async Task<IActionResult> Get([FromQuery] Guid connectionId, CancellationToken ct = default)
    {
        if (connectionId == Guid.Empty)
            return BadRequest(new { error = "connectionId is required" });

        var query = await _service.GetQueryableAsync(CurrentUserId, connectionId, ct);
        return Ok(query);
    }
}
