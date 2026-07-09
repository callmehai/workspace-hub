using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.DTOs.Drive;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Api.Controllers
{
    /// <summary>
    /// Google Drive — tạo folder + chia sẻ permissions (SCRUM-79 — A5).
    /// Controller mỏng: validate → gọi <see cref="IDriveSharingService"/> → trả kết quả.
    /// </summary>
    [Authorize]
    public class DriveController : ApiControllerBase
    {
        private readonly IDriveSharingService _driveSharing;
        private readonly IValidator<CreateDriveFolderRequest> _createFolderValidator;
        private readonly IValidator<AddDrivePermissionRequest> _addPermissionValidator;
        private readonly IValidator<UpdateDrivePermissionRequest> _updatePermissionValidator;
        private readonly IValidator<LinkSharingRequest> _linkSharingValidator;
        public DriveController(
            IDriveSharingService driveSharing,
            IValidator<CreateDriveFolderRequest> createFolderValidator,
            IValidator<AddDrivePermissionRequest> addPermissionValidator,
            IValidator<UpdateDrivePermissionRequest> updatePermissionValidator,
            IValidator<LinkSharingRequest> linkSharingValidator)
        {
            _driveSharing = driveSharing;
            _createFolderValidator = createFolderValidator;
            _addPermissionValidator = addPermissionValidator;
            _updatePermissionValidator = updatePermissionValidator;
            _linkSharingValidator = linkSharingValidator;
        }

        /// <summary>POST /api/drive/folders — tạo folder trên Google Drive.</summary>
        [HttpPost("folders")]
        public async Task<ActionResult<ItemResponse>> CreateFolder(
            [FromBody] CreateDriveFolderRequest request,
            CancellationToken ct = default)
        {
            await _createFolderValidator.ValidateAndThrowAsync(request, ct);
            var created = await _driveSharing.CreateFolderAsync(
                CurrentUserId,
                request.ConnectionId,
                request.Name,
                request.ParentItemId,
                ct);
            // 201 + Location trỏ về GET /api/items/{id}
            return CreatedAtAction(
                nameof(ItemsController.GetItemById),
                "Items",
                new { id = created.Id },
                created);
        }

        /// <summary>GET /api/drive/items/{itemId}/permissions — danh sách quyền share.</summary>
        [HttpGet("items/{itemId:guid}/permissions")]
        public async Task<ActionResult<DrivePermissionsListResponse>> ListPermissions(
            Guid itemId,
            CancellationToken ct = default)
        {
            var result = await _driveSharing.ListPermissionsAsync(CurrentUserId, itemId, ct);
            return Ok(result);
        }

        /// <summary>POST /api/drive/items/{itemId}/permissions — mời email chia sẻ.</summary>
        [HttpPost("items/{itemId:guid}/permissions")]
        public async Task<ActionResult<DrivePermissionDto>> AddPermission(
            Guid itemId,
            [FromBody] AddDrivePermissionRequest request,
            CancellationToken ct = default)
        {
            await _addPermissionValidator.ValidateAndThrowAsync(request, ct);
            var created = await _driveSharing.AddPermissionAsync(
                CurrentUserId,
                itemId,
                request.Email,
                ParseRole(request.Role),
                request.Notify,
                ct);
            return CreatedAtAction(
                nameof(ListPermissions),
                new { itemId },
                created);
        }

        /// <summary>PATCH /api/drive/items/{itemId}/permissions/{permissionId} — đổi role.</summary>
        [HttpPatch("items/{itemId:guid}/permissions/{permissionId}")]
        public async Task<ActionResult<DrivePermissionDto>> UpdatePermission(
            Guid itemId,
            string permissionId,
            [FromBody] UpdateDrivePermissionRequest request,
            CancellationToken ct = default)
        {
            await _updatePermissionValidator.ValidateAndThrowAsync(request, ct);
            var updated = await _driveSharing.UpdatePermissionAsync(
                CurrentUserId,
                itemId,
                permissionId,
                ParseRole(request.Role),
                ct);
            return Ok(updated);
        }

        /// <summary>DELETE /api/drive/items/{itemId}/permissions/{permissionId} — gỡ quyền.</summary>
        [HttpDelete("items/{itemId:guid}/permissions/{permissionId}")]
        public async Task<IActionResult> RemovePermission(
            Guid itemId,
            string permissionId,
            CancellationToken ct = default)
        {
            await _driveSharing.RemovePermissionAsync(CurrentUserId, itemId, permissionId, ct);
            return NoContent();
        }

        /// <summary>PUT /api/drive/items/{itemId}/link-sharing — bật/tắt link công khai.</summary>
        [HttpPut("items/{itemId:guid}/link-sharing")]
        public async Task<ActionResult<DrivePermissionDto>> SetLinkSharing(
            Guid itemId,
            [FromBody] LinkSharingRequest request,
            CancellationToken ct = default)
        {
            await _linkSharingValidator.ValidateAndThrowAsync(request, ct);
            var role = request.Enabled
                ? ParseRole(request.Role!)
                : DrivePermissionRole.Reader;
            var result = await _driveSharing.SetLinkSharingAsync(
                CurrentUserId,
                itemId,
                request.Enabled,
                role,
                ct);
            return Ok(result);
        }

        /// <summary>Chuyển role string (JSON) → enum — validator đã check trước đó.</summary>
        private static DrivePermissionRole ParseRole(string role)
        {
            if (!DrivePermissionRoles.TryParse(role, out var parsed))
                throw new InvalidOperationException("Role không hợp lệ sau validation.");
            return parsed;
        }


    }
}
