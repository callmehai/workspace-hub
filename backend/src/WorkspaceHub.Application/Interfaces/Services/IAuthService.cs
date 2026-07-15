using WorkspaceHub.Application.DTOs.Auth;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>Hợp đồng auth — register, login, Google sign-in. JWT generation nằm bên trong implementation.</summary>
public interface IAuthService
{
    /// <summary>SCRUM-64: tạo user (PhoneVerified=false) + gửi OTP. KHÔNG phát token — chờ verify.</summary>
    Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<UserDto> GetMeAsync(Guid userId, CancellationToken ct = default);

    /// <summary>SCRUM-64: gửi lại OTP cho user chưa verify (theo email). Trả cooldown (giây).</summary>
    Task<int> SendOtpAsync(string email, CancellationToken ct = default);

    /// <summary>SCRUM-64: verify OTP → set PhoneVerified=true → phát JWT (đăng nhập luôn).</summary>
    Task<AuthResponse> VerifyOtpAsync(string email, string code, CancellationToken ct = default);

    /// <summary>Build Google Sign-In authorization URL + cache CSRF state.</summary>
    Task<GoogleAuthStartResponse> GoogleStartAsync(CancellationToken ct = default);

    /// <summary>Exchange code → verify id_token → find/create/link user → issue JWT.</summary>
    Task<AuthResponse> GoogleCallbackAsync(string code, string state, CancellationToken ct = default);
}
