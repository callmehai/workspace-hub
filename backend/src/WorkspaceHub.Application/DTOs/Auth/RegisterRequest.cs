namespace WorkspaceHub.Application.DTOs.Auth;

/// <summary>Yêu cầu đăng ký tài khoản mới (SCRUM-64: thêm Phone để xác minh OTP).</summary>
public record RegisterRequest(string Email, string Password, string FullName, string Phone, string? InviteToken = null);
