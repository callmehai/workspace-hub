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
    /// Google Drive — tạo folder, upload file/folder, chia sẻ permissions.
    /// Controller mỏng: nhận request → gọi service → trả kết quả.
    /// </summary>
    [Authorize]
    public class DriveController : ApiControllerBase
    {
        private readonly IDriveSharingService _driveSharing;
        private readonly IDriveUploadService _driveUpload;
        private readonly IValidator<CreateDriveFolderRequest> _createFolderValidator;
        private readonly IValidator<AddDrivePermissionRequest> _addPermissionValidator;
        private readonly IValidator<UpdateDrivePermissionRequest> _updatePermissionValidator;
        private readonly IValidator<LinkSharingRequest> _linkSharingValidator;
        public DriveController(
            IDriveSharingService driveSharing,
            IDriveUploadService driveUpload,
            IValidator<CreateDriveFolderRequest> createFolderValidator,
            IValidator<AddDrivePermissionRequest> addPermissionValidator,
            IValidator<UpdateDrivePermissionRequest> updatePermissionValidator,
            IValidator<LinkSharingRequest> linkSharingValidator)
        {
            _driveSharing = driveSharing;
            _driveUpload = driveUpload;
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

        /// <summary>
        /// POST /api/drive/files — upload một file từ máy lên Google Drive.
        /// multipart/form-data: connectionId, parentItemId? (optional), file.
        /// Luồng: nhận IFormFile → stream xuống DriveUploadService → 201 + Item mới.
        /// </summary>
        [HttpPost("files")]
        [RequestSizeLimit(DriveUploadLimits.RequestSizeSingleFileBytes)]
        public async Task<ActionResult<ItemResponse>> UploadFile(
            [FromForm] Guid connectionId,
            [FromForm] Guid? parentItemId,
            IFormFile file,
            CancellationToken ct = default)
        {
            if (file is null || file.Length == 0)
                return BadRequest("Thiếu file.");

            await using var stream = file.OpenReadStream();
            var created = await _driveUpload.UploadFileAsync(
                CurrentUserId,
                connectionId,
                file.FileName,
                file.ContentType ?? "application/octet-stream",
                stream,
                file.Length,
                parentItemId,
                ct);

            return CreatedAtAction(
                nameof(ItemsController.GetItemById),
                "Items",
                new { id = created.Id },
                created);
        }

        /// <summary>
        /// POST /api/drive/folders/upload — upload cả folder từ máy (webkitdirectory).
        /// multipart/form-data: connectionId, parentItemId?, files[], paths[].
        /// paths[i] = đường dẫn tương đối của files[i] (vd. DuAn/docs/readme.pdf).
        /// </summary>
        [HttpPost("folders/upload")]
        [RequestSizeLimit(DriveUploadLimits.RequestSizeFolderUploadBytes)]
        public async Task<ActionResult<DriveFolderUploadResponse>> UploadFolder(
            [FromForm] Guid connectionId,
            [FromForm] Guid? parentItemId,
            [FromForm] List<IFormFile> files,
            [FromForm] List<string> paths,
            CancellationToken ct = default)
        {
            if (files is null || files.Count == 0)
                return BadRequest("Thiếu file.");

            if (paths is null || paths.Count != files.Count)
                return BadRequest("Số lượng paths phải khớp số file.");

            var entries = new List<DriveFolderUploadEntry>(files.Count);
            for (var i = 0; i < files.Count; i++)
            {
                var formFile = files[i];
                if (formFile.Length == 0)
                    return BadRequest($"File rỗng: {formFile.FileName}");

                entries.Add(new DriveFolderUploadEntry(
                    paths[i],
                    formFile.FileName,
                    formFile.ContentType ?? "application/octet-stream",
                    formFile.OpenReadStream(),
                    formFile.Length));
            }

            try
            {
                var result = await _driveUpload.UploadFolderAsync(
                    CurrentUserId,
                    connectionId,
                    entries,
                    parentItemId,
                    ct);
                return Ok(result);
            }
            finally
            {
                // Stream mở từ IFormFile — dispose sau khi service upload xong.
                foreach (var entry in entries)
                    await entry.Content.DisposeAsync();
            }
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
