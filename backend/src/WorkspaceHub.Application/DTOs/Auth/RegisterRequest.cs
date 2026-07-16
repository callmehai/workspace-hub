namespace WorkspaceHub.Application.DTOs.Auth;

/// <summary>Yêu cầu đăng ký tài khoản mới (SCRUM-64: OTP xác minh qua email, không cần phone).</summary>
public record RegisterRequest(string Email, string Password, string FullName, string? InviteToken = null);
