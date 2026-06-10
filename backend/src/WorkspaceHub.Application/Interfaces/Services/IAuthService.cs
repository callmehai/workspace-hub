using WorkspaceHub.Application.DTOs.Auth;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>Hợp đồng auth — register, login. JWT generation nằm bên trong implementation.</summary>
public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
}
