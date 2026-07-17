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
        // Nâng luôn MultipartBodyLengthLimit cho khớp (file 100MB < 128MB nên hiếm đụng, nhưng để nhất quán).
        [RequestFormLimits(MultipartBodyLengthLimit = DriveUploadLimits.RequestSizeSingleFileBytes)]
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
        // MultipartBodyLengthLimit mặc định 128MB — không nâng thì folder tổng >128MB bị multipart
        // reader ném InvalidDataException TRƯỚC khi vào action (400 khó hiểu). Nâng khớp giới hạn 500MB.
        [RequestFormLimits(MultipartBodyLengthLimit = DriveUploadLimits.RequestSizeFolderUploadBytes)]
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

            // Validate giới hạn TRƯỚC khi OpenReadStream — tránh mở hàng trăm stream rồi mới 422.
            if (files.Count > DriveUploadLimits.MaxFolderFileCount)
                return BadRequest($"Tối đa {DriveUploadLimits.MaxFolderFileCount} file mỗi lần upload folder.");

            long totalBytes = 0;
            foreach (var formFile in files)
            {
                if (formFile.Length == 0)
                    return BadRequest($"File rỗng: {formFile.FileName}");
                if (formFile.Length > DriveUploadLimits.MaxFileBytes)
                    return BadRequest($"File vượt quá 100 MB: {formFile.FileName}");
                totalBytes += formFile.Length;
            }

            if (totalBytes > DriveUploadLimits.MaxFolderTotalBytes)
                return BadRequest("Tổng dung lượng upload folder vượt quá 500 MB.");

            var entries = new List<DriveFolderUploadEntry>(files.Count);
            try
            {
                for (var i = 0; i < files.Count; i++)
                {
                    var formFile = files[i];
                    entries.Add(new DriveFolderUploadEntry(
                        paths[i],
                        formFile.FileName,
                        formFile.ContentType ?? "application/octet-stream",
                        formFile.OpenReadStream(),
                        formFile.Length));
                }

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
                // Dispose mọi stream đã mở (kể cả khi build entries lỗi giữa chừng).
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
        /// <remarks>
        /// Tắt link (enabled=false): nếu Case 1 (folder mẹ đang anyone) mà chưa
        /// <c>confirmRestrictParent</c> → 409 + body <see cref="DriveLinkRestrictConflict"/> (FE hiện popup).
        /// Sau khi user bấm "Xoá khỏi thư mục mẹ" → gọi lại với <c>confirmRestrictParent: true</c>
        /// → tắt link cả file lẫn folder mẹ.
        /// </remarks>
        [HttpPut("items/{itemId:guid}/link-sharing")]
        [ProducesResponseType(typeof(DrivePermissionDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(DriveLinkRestrictConflict), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<DrivePermissionDto>> SetLinkSharing(
            Guid itemId,
            [FromBody] LinkSharingRequest request,
            CancellationToken ct = default)
        {
            await _linkSharingValidator.ValidateAndThrowAsync(request, ct);

            var role = request.Enabled
                ? ParseRole(request.Role!)
                : DrivePermissionRole.Reader;
            // Case 1 chưa confirm → service ném ConflictException(Payload=DriveLinkRestrictConflict)
            // → ExceptionMiddleware trả 409 body = DTO (FE hiện popup).
            var result = await _driveSharing.SetLinkSharingAsync(
                CurrentUserId,
                itemId,
                request.Enabled,
                role,
                request.ConfirmRestrictParent,
                skipConflictDetect: false,
                ct);
            return Ok(result);
        }

        /// <summary>
        /// GET /api/drive/items/{itemId}/link-sharing/restrict-conflict —
        /// Preview Case 1 (tắt link file có kéo theo folder mẹ không).
        /// 200 + conflict DTO, hoặc 204 nếu không xung đột (tắt link thẳng được).
        /// </summary>
        [HttpGet("items/{itemId:guid}/link-sharing/restrict-conflict")]
        [ProducesResponseType(typeof(DriveLinkRestrictConflict), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult<DriveLinkRestrictConflict>> GetLinkRestrictConflict(
            Guid itemId,
            CancellationToken ct = default)
        {
            var conflict = await _driveSharing.DetectLinkRestrictConflictAsync(
                CurrentUserId, itemId, ct);
            if (conflict is null)
                return NoContent();
            return Ok(conflict);
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
