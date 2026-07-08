using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Auth;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>Hồ sơ user hiện tại — đổi tên, đổi mật khẩu, avatar (SCRUM-75).</summary>
[Authorize]
public class UsersController : ApiControllerBase
{
    private readonly IUserProfileService _profile;

    public UsersController(IUserProfileService profile)
    {
        _profile = profile;
    }

    /// <summary>PATCH /api/users/me — đổi họ tên.</summary>
    [HttpPatch("me")]
    [ProducesResponseType(typeof(UserDto), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<UserDto>> UpdateProfile(UpdateProfileRequest request, CancellationToken ct)
        => Ok(await _profile.UpdateProfileAsync(CurrentUserId, request.FullName, ct));

    /// <summary>POST /api/users/me/change-password — 204. 422 sai mật khẩu hiện tại / tài khoản Google không có mật khẩu.</summary>
    [HttpPost("me/change-password")]
    [ProducesResponseType(204)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await _profile.ChangePasswordAsync(CurrentUserId, request.CurrentPassword, request.NewPassword, ct);
        return NoContent();
    }

    /// <summary>POST /api/users/me/avatar — upload ảnh đại diện (multipart/form-data, field "file").</summary>
    [HttpPost("me/avatar")]
    [ProducesResponseType(typeof(UserDto), 200)]
    [ProducesResponseType(422)]
    public async Task<ActionResult<UserDto>> UploadAvatar(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new BusinessRuleException("Thiếu file ảnh.");

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension))
            extension = file.ContentType == "image/png" ? ".png" : file.ContentType == "image/webp" ? ".webp" : ".jpg";

        await using var stream = file.OpenReadStream();
        return Ok(await _profile.UploadAvatarAsync(CurrentUserId, stream, file.ContentType, extension, ct));
    }

    /// <summary>DELETE /api/users/me/avatar — xoá ảnh đại diện hiện tại.</summary>
    [HttpDelete("me/avatar")]
    [ProducesResponseType(typeof(UserDto), 200)]
    public async Task<ActionResult<UserDto>> DeleteAvatar(CancellationToken ct)
        => Ok(await _profile.DeleteAvatarAsync(CurrentUserId, ct));
}
