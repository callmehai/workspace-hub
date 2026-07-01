namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// Quản lý refresh token (SCRUM-63). Refresh token = JWT ký, mang <c>jti</c> ngẫu nhiên;
/// Redis lưu <c>refresh:{jti}</c> → metadata (userId, family) làm "danh sách còn hiệu lực".
/// Revoke = xoá key Redis. Rotation = mỗi lần refresh cấp jti mới + xoá jti cũ.
/// Reuse jti đã revoke (token theft) → revoke cả family.
///
/// Chốt: dùng JWT+jti (xem CHANGELOG [2026-06-30]), KHÔNG opaque-token-hash.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>Phát refresh token mới cho user (login/register/google) — tạo family mới.</summary>
    Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Verify refresh token + xoay vòng: cấp access token mới + refresh token mới,
    /// revoke jti cũ. Token không hợp lệ / đã revoke / reuse → throw (middleware map 401).
    /// </summary>
    Task<RotatedTokens> ValidateAndRotateAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>Revoke refresh token (logout). Bỏ qua nếu token rỗng/không hợp lệ.</summary>
    Task RevokeAsync(string? refreshToken, CancellationToken ct = default);
}

/// <summary>Refresh token vừa phát + thời hạn (giây) để set cookie.</summary>
public record IssuedRefreshToken(string RefreshToken, int ExpiresInSeconds);

/// <summary>Kết quả sau khi xoay vòng: access token mới + refresh token mới.</summary>
public record RotatedTokens(
    string AccessToken,
    int AccessExpiresInSeconds,
    string RefreshToken,
    int RefreshExpiresInSeconds,
    UserId User);

/// <summary>Thông tin user tối thiểu trả về sau refresh (để FE cập nhật nếu cần).</summary>
public record UserId(Guid Id, string Email, string FullName, string Role);
