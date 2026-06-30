using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// Phát access token JWT cho user (SCRUM-63). Nguồn DUY NHẤT sinh access token —
/// AuthService (login/register/google) lẫn RefreshTokenService (/auth/refresh) đều dùng
/// để claim/TTL không lệch giữa 2 luồng (review SCRUM-63 #3).
/// </summary>
public interface IJwtTokenFactory
{
    /// <summary>Trả (token, expiresInSeconds). Claims: sub, email, role.</summary>
    (string Token, int ExpiresInSeconds) CreateAccessToken(User user);
}
