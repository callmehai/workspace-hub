using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>
/// Folder CRUD endpoints (SCRUM-18).
/// Controller mỏng: nhận request → validate → gọi service → trả kết quả.
/// KHÔNG chứa business logic (xem CONVENTIONS.md).
/// </summary>
[Authorize]
public class FoldersController : BaseApiController
{
    private readonly IFolderService _folderService;
    private readonly IValidator<CreateFolderRequest> _createValidator;
    private readonly IValidator<UpdateFolderRequest> _updateValidator;

    public FoldersController(
        IFolderService folderService,
        IValidator<CreateFolderRequest> createValidator,
        IValidator<UpdateFolderRequest> updateValidator)
    {
        _folderService = folderService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>
    /// GET /api/folders?includeShared=true
    /// Trả array folders owned by hoặc shared with user.
    /// Hỗ trợ OData: $filter, $orderby, $top, $skip, $select, $count.
    /// </summary>
    [HttpGet]
    [EnableQuery]
    public async Task<ActionResult<IEnumerable<FolderResponse>>> GetFolders(
        [FromQuery] bool includeShared = false,
        CancellationToken ct = default)
    {
        var folders = await _folderService.GetFoldersAsync(UserId, includeShared, ct);
        return Ok(folders);
    }

    /// <summary>
    /// POST /api/folders — tạo folder mới.
    /// Trả 201 Created với folder vừa tạo.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<FolderResponse>> CreateFolder(
        [FromBody] CreateFolderRequest request,
        CancellationToken ct = default)
    {
        // Validate input qua FluentValidation
        var validation = await _createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .Select(e => new { field = e.PropertyName, issue = e.ErrorMessage });
            return BadRequest(new { error = "ValidationError", message = "Validation failed.", details = errors });
        }

        var created = await _folderService.CreateAsync(UserId, request, ct);
        return CreatedAtAction(nameof(GetFolders), null, created);
    }

    /// <summary>
    /// PUT /api/folders/{id} — cập nhật folder metadata.
    /// Restricted to Owner only; 403 Forbidden nếu user khác.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<FolderResponse>> UpdateFolder(
        Guid id,
        [FromBody] UpdateFolderRequest request,
        CancellationToken ct = default)
    {
        // Validate input qua FluentValidation
        var validation = await _updateValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .Select(e => new { field = e.PropertyName, issue = e.ErrorMessage });
            return BadRequest(new { error = "ValidationError", message = "Validation failed.", details = errors });
        }

        var updated = await _folderService.UpdateAsync(UserId, id, request, ct);
        return Ok(updated);
    }

    /// <summary>
    /// DELETE /api/folders/{id} — xoá folder (hard delete).
    /// CASCADE: ItemFolders + FolderShares bị xoá, Items giữ lại.
    /// Restricted to Owner only; 403 Forbidden nếu user khác.
    /// Trả 204 No Content.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteFolder(
        Guid id,
        CancellationToken ct = default)
    {
        await _folderService.DeleteAsync(UserId, id, ct);
        return NoContent();
    }
}
