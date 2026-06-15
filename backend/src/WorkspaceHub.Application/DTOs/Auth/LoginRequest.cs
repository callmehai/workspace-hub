namespace WorkspaceHub.Application.DTOs.Auth;

/// <summary>Yêu cầu đăng nhập.</summary>
public record LoginRequest(string Email, string Password);
