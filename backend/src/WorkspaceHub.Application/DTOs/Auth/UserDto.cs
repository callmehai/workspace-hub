namespace WorkspaceHub.Application.DTOs.Auth;

/// <summary>Thông tin user trả về client (KHÔNG chứa PasswordHash).</summary>
public record UserDto(Guid Id, string Email, string FullName, string Role, string? AvatarUrl, string? AuthProvider);
