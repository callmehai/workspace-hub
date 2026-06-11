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
public class ItemsController : BaseApiController
{
    private readonly IItemService _itemService;
    private readonly IValidator<GetItemsRequest> _validator;

    public ItemsController(
        IItemService itemService,
        IValidator<GetItemsRequest> validator)
    {
        _itemService = itemService;
        _validator = validator;
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
        // Validate query parameters qua FluentValidation
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .Select(e => new { field = e.PropertyName, issue = e.ErrorMessage });
            return BadRequest(new
            {
                error = "ValidationError",
                message = "Validation failed.",
                details = errors
            });
        }

        var result = await _itemService.GetItemsAsync(UserId, request, ct);
        return Ok(result);
    }
}
