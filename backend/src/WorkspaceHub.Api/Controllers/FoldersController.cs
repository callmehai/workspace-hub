using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Attributes;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>
/// Folder CRUD + Sharing endpoints (SCRUM-18).
/// Controller mỏng: nhận request → validate → gọi service → trả kết quả.
/// KHÔNG chứa business logic (xem CONVENTIONS.md).
/// </summary>
[Authorize]
[ODataIgnored]
public class FoldersController : ApiControllerBase
{
    private readonly IFolderService _folderService;
    private readonly IValidator<CreateFolderRequest> _createValidator;
    private readonly IValidator<UpdateFolderRequest> _updateValidator;
    private readonly IValidator<AddItemToFolderRequest> _addItemValidator;
    private readonly IValidator<InviteFolderShareRequest> _inviteShareValidator;
    private readonly IValidator<UpdateFolderShareRequest> _updateShareValidator;

    public FoldersController(
        IFolderService folderService,
        IValidator<CreateFolderRequest> createValidator,
        IValidator<UpdateFolderRequest> updateValidator,
        IValidator<AddItemToFolderRequest> addItemValidator,
        IValidator<InviteFolderShareRequest> inviteShareValidator,
        IValidator<UpdateFolderShareRequest> updateShareValidator)
    {
        _folderService = folderService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _addItemValidator = addItemValidator;
        _inviteShareValidator = inviteShareValidator;
        _updateShareValidator = updateShareValidator;
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
        var folders = await _folderService.GetFoldersAsync(CurrentUserId, includeShared, ct);
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
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var created = await _folderService.CreateAsync(CurrentUserId, request, ct);
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
        await _updateValidator.ValidateAndThrowAsync(request, ct);
        var updated = await _folderService.UpdateAsync(CurrentUserId, id, request, ct);
        return Ok(updated);
    }

    /// <summary>
    /// DELETE /api/folders/{id} — xoá folder (hard delete).
    /// CASCADE: ItemFolders + FolderShares bị xoá, Items giữ lại.
    /// Restricted to Owner only; 403 Forbidden nếu user khác.
    /// Trả 204 No Content.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteFolder(Guid id, CancellationToken ct = default)
    {
        await _folderService.DeleteAsync(CurrentUserId, id, ct);
        return NoContent();
    }

    /// <summary>POST /api/folders/{id}/items — gắn item vào folder.</summary>
    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<ItemFolderResponse>> AddItemToFolder(
        Guid id,
        [FromBody] AddItemToFolderRequest request,
        CancellationToken ct = default)
    {
        await _addItemValidator.ValidateAndThrowAsync(request, ct);
        var result = await _folderService.AddItemToFolderAsync(CurrentUserId, id, request, ct);
        return StatusCode(201, result);
    }

    /// <summary>DELETE /api/folders/{id}/items/{itemId} — gỡ item khỏi folder.</summary>
    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> RemoveItemFromFolder(
        Guid id, Guid itemId, CancellationToken ct = default)
    {
        await _folderService.RemoveItemFromFolderAsync(CurrentUserId, id, itemId, ct);
        return NoContent();
    }

    /// <summary>POST /api/folders/{id}/items/bulk — gắn nhiều item vào folder.</summary>
    [HttpPost("{id:guid}/items/bulk")]
    public async Task<IActionResult> AddItemsToFolderBulk(
        Guid id,
        [FromBody] AddItemsToFolderBulkRequest request,
        CancellationToken ct = default)
    {
        if (request.ItemIds == null || !request.ItemIds.Any())
            return BadRequest(new { Message = "ItemIds is required and cannot be empty." });

        await _folderService.AddItemsToFolderAsync(CurrentUserId, id, request, ct);
        return Ok();
    }

    /// <summary>DELETE /api/folders/{id}/items/bulk — gỡ nhiều item khỏi folder.</summary>
    [HttpDelete("{id:guid}/items/bulk")]
    public async Task<IActionResult> RemoveItemsFromFolderBulk(
        Guid id,
        [FromBody] RemoveItemsFromFolderBulkRequest request,
        CancellationToken ct = default)
    {
        if (request.ItemIds == null || !request.ItemIds.Any())
            return BadRequest(new { Message = "ItemIds is required and cannot be empty." });

        await _folderService.RemoveItemsFromFolderAsync(CurrentUserId, id, request, ct);
        return NoContent();
    }

    // ─────────────────────────── Sharing Endpoints ───────────────────────────
    // Literal routes (shared-with-me, shares/*) phải khai báo TRƯỚC template routes ({id:guid})
    // để ASP.NET Core routing ưu tiên literal segment.

    /// <summary>
    /// GET /api/folders/shared-with-me
    /// Danh sách folder được chia sẻ với user hiện tại (chỉ đã accept).
    /// </summary>
    [HttpGet("shared-with-me")]
    public async Task<ActionResult<IEnumerable<SharedFolderDto>>> GetSharedWithMe(CancellationToken ct = default)
    {
        var shared = await _folderService.GetFoldersSharedWithMeAsync(CurrentUserId, ct);
        return Ok(shared);
    }

    /// <summary>
    /// POST /api/folders/shares/{shareId}/accept
    /// Chấp nhận invite share. Chỉ người được mời.
    /// 403 nếu không phải người được mời. 409 nếu đã accept.
    /// </summary>
    [HttpPost("shares/{shareId:guid}/accept")]
    public async Task<ActionResult<FolderShareDto>> AcceptShare(
        Guid shareId, CancellationToken ct = default)
    {
        var result = await _folderService.AcceptShareAsync(shareId, CurrentUserId, ct);
        return Ok(result);
    }

    /// <summary>
    /// POST /api/folders/shares/{shareId}/decline
    /// Từ chối invite share (xoá row). Chỉ người được mời. 204 No Content.
    /// </summary>
    [HttpPost("shares/{shareId:guid}/decline")]
    public async Task<IActionResult> DeclineShare(
        Guid shareId, CancellationToken ct = default)
    {
        await _folderService.DeclineShareAsync(shareId, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>
    /// POST /api/folders/{id}/shares
    /// Mời bạn bè vào folder. Chỉ Owner.
    /// 201 + FolderShareDto (status="Pending"). 409 đã share, 422 không phải bạn bè.
    /// </summary>
    [HttpPost("{id:guid}/shares")]
    public async Task<ActionResult<FolderShareDto>> InviteShare(
        Guid id,
        [FromBody] InviteFolderShareRequest request,
        CancellationToken ct = default)
    {
        await _inviteShareValidator.ValidateAndThrowAsync(request, ct);
        var result = await _folderService.InviteShareAsync(id, CurrentUserId, request, ct);
        return StatusCode(201, result);
    }

    /// <summary>
    /// GET /api/folders/{id}/shares
    /// Danh sách ai được share folder này. Chỉ Owner. 403 nếu không phải owner.
    /// </summary>
    [HttpGet("{id:guid}/shares")]
    public async Task<ActionResult<IEnumerable<FolderShareDto>>> GetShares(
        Guid id, CancellationToken ct = default)
    {
        var shares = await _folderService.GetSharesForFolderAsync(id, CurrentUserId, ct);
        return Ok(shares);
    }

    /// <summary>
    /// PATCH /api/folders/{id}/shares/{shareId}
    /// Đổi quyền 1 share (Viewer ↔ Editor). Chỉ Owner. Body: { permission }.
    /// </summary>
    [HttpPatch("{id:guid}/shares/{shareId:guid}")]
    public async Task<ActionResult<FolderShareDto>> UpdateShareRole(
        Guid id,
        Guid shareId,
        [FromBody] UpdateFolderShareRequest request,
        CancellationToken ct = default)
    {
        await _updateShareValidator.ValidateAndThrowAsync(request, ct);
        var result = await _folderService.UpdateShareRoleAsync(id, shareId, CurrentUserId, request, ct);
        return Ok(result);
    }

    /// <summary>
    /// DELETE /api/folders/{id}/shares/{shareId}
    /// Revoke share. Chỉ Owner. 204 No Content.
    /// </summary>
    [HttpDelete("{id:guid}/shares/{shareId:guid}")]
    public async Task<IActionResult> RevokeShare(
        Guid id, Guid shareId, CancellationToken ct = default)
    {
        await _folderService.RevokeShareAsync(id, shareId, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>
    /// DELETE /api/folders/{id}/leave
    /// Rời khỏi folder được chia sẻ. 204 No Content.
    /// </summary>
    [HttpDelete("{id:guid}/leave")]
    public async Task<IActionResult> LeaveFolder(
        Guid id, CancellationToken ct = default)
    {
        await _folderService.LeaveFolderAsync(id, CurrentUserId, ct);
        return NoContent();
    }
}
