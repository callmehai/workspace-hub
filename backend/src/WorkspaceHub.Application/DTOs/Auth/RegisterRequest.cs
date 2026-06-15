namespace WorkspaceHub.Application.DTOs.Auth;

/// <summary>Yêu cầu đăng ký tài khoản mới.</summary>
public record RegisterRequest(string Email, string Password, string FullName);
