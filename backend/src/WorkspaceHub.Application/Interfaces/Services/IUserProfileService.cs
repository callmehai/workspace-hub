using WorkspaceHub.Application.DTOs.Auth;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>Quản lý hồ sơ user: avatar (SCRUM-75, Cloudflare R2), đổi tên, đổi mật khẩu.</summary>
public interface IUserProfileService
{
    Task<UserDto> UploadAvatarAsync(Guid userId, Stream content, string contentType, string fileExtension, CancellationToken ct = default);

    Task<UserDto> DeleteAvatarAsync(Guid userId, CancellationToken ct = default);

    Task<UserDto> UpdateProfileAsync(Guid userId, string fullName, CancellationToken ct = default);

    /// <summary>Chỉ áp dụng user có PasswordHash (AuthProvider Local/Both). Throw nếu tài khoản chỉ đăng nhập Google hoặc sai mật khẩu hiện tại.</summary>
    Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct = default);
}
