namespace WorkspaceHub.Application.DTOs.Auth;

/// <summary>Response sau register/login thành công — JWT + user info.</summary>
public record AuthResponse(string AccessToken, int ExpiresIn, UserDto User);
