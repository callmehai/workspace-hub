using WorkspaceHub.Application.DTOs.Auth;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>Hợp đồng auth — register, login, Google sign-in. JWT generation nằm bên trong implementation.</summary>
public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<UserDto> GetMeAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Build Google Sign-In authorization URL + cache CSRF state.</summary>
    Task<GoogleAuthStartResponse> GoogleStartAsync(CancellationToken ct = default);

    /// <summary>Exchange code → verify id_token → find/create/link user → issue JWT.</summary>
    Task<AuthResponse> GoogleCallbackAsync(string code, string state, CancellationToken ct = default);
}
