using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Auth;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Services;

/// <summary>Avatar upload/xoá qua Cloudflare R2 (SCRUM-75) + đổi tên/đổi mật khẩu.</summary>
public class UserProfileService : IUserProfileService
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp",
    };
    private const long MaxAvatarBytes = 5 * 1024 * 1024; // 5MB

    private readonly IUserRepository _users;
    private readonly IFileStorageService _storage;

    public UserProfileService(IUserRepository users, IFileStorageService storage)
    {
        _users = users;
        _storage = storage;
    }

    public async Task<UserDto> UploadAvatarAsync(
        Guid userId, Stream content, string contentType, string fileExtension, CancellationToken ct = default)
    {
        if (!AllowedContentTypes.Contains(contentType))
            throw new BusinessRuleException("Chỉ chấp nhận ảnh JPEG, PNG hoặc WebP.");

        if (content.Length > MaxAvatarBytes)
            throw new BusinessRuleException("Ảnh đại diện tối đa 5MB.");

        var user = await _users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException(nameof(User), userId);

        var previousKey = user.AvatarUrl is not null ? ExtractKey(user.AvatarUrl) : null;

        // Key theo userId + extension → cùng định dạng thì ghi đè (URL vẫn đổi nhờ query cache-bust
        // ?v=ETag, xem R2FileStorageService). Đổi định dạng (vd .png → .jpg) ra key khác → phải xoá
        // object cũ tay để không rác trên R2. So sánh theo KEY (không phải URL) vì URL luôn đổi.
        var key = $"avatars/{userId}{fileExtension}";
        var url = await _storage.UploadAsync(key, content, contentType, ct);

        user.AvatarUrl = url;
        await _users.SaveChangesAsync(ct);

        if (previousKey is not null && previousKey != key)
            await _storage.DeleteAsync(previousKey, ct);

        return MapToDto(user);
    }

    public async Task<UserDto> DeleteAvatarAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException(nameof(User), userId);

        if (user.AvatarUrl is not null)
        {
            await _storage.DeleteAsync(ExtractKey(user.AvatarUrl), ct);
            user.AvatarUrl = null;
            await _users.SaveChangesAsync(ct);
        }

        return MapToDto(user);
    }

    public async Task<UserDto> UpdateProfileAsync(Guid userId, string fullName, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException(nameof(User), userId);

        user.FullName = fullName.Trim();
        await _users.SaveChangesAsync(ct);

        return MapToDto(user);
    }

    public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException(nameof(User), userId);

        if (string.IsNullOrEmpty(user.PasswordHash))
            throw new BusinessRuleException("Tài khoản đăng nhập qua Google, không có mật khẩu để đổi.");

        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            throw new BusinessRuleException("Mật khẩu hiện tại không đúng.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 12);
        await _users.SaveChangesAsync(ct);
    }

    private static string ExtractKey(string avatarUrl) => new Uri(avatarUrl).AbsolutePath.TrimStart('/');

    private static UserDto MapToDto(User user)
        => new(user.Id, user.Email, user.FullName, user.Role.ToString(), user.AvatarUrl, user.AuthProvider.ToString());
}
