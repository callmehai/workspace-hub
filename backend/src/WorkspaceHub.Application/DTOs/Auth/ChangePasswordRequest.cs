namespace WorkspaceHub.Application.DTOs.Auth;

/// <summary>POST /api/users/me/change-password. Chỉ áp dụng cho user có PasswordHash (AuthProvider Local/Both).</summary>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
