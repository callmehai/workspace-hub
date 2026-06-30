using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Infrastructure.Services;

/// <summary>
/// SCRUM-63 — refresh token service (JWT + jti, danh sách hiệu lực ở Redis qua IDistributedCache).
///
/// Khoá Redis:
///   refresh:{jti}    → "{userId}:{familyId}"   (1 refresh token còn sống)
///   refreshfam:{fam} → "1"                       (marker family; xoá = revoke cả family)
///
/// Rotation: mỗi lần refresh cấp jti mới (cùng family) + xoá jti cũ.
/// Reuse detection: token ký hợp lệ + chưa hết hạn nhưng jti KHÔNG còn trong Redis
///   → coi là token đã bị dùng lại (theft) → revoke cả family (xoá marker) → mọi
///   refresh token cùng family thành vô hiệu.
/// </summary>
public class RefreshTokenService : IRefreshTokenService
{
    private readonly IConfiguration _config;
    private readonly IDistributedCache _cache;
    private readonly IUserRepository _users;
    private readonly ILogger<RefreshTokenService> _logger;

    private const string FamilyClaim = "fam";

    public RefreshTokenService(
        IConfiguration config,
        IDistributedCache cache,
        IUserRepository users,
        ILogger<RefreshTokenService> logger)
    {
        _config = config;
        _cache = cache;
        _users = users;
        _logger = logger;
    }

    public async Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken ct = default)
    {
        var familyId = Guid.NewGuid().ToString("N");
        var (token, _, expiresIn) = await CreateAndStoreRefreshTokenAsync(userId, familyId, ct);
        _logger.LogInformation("Issued refresh token. UserId={UserId}, Family={Family}", userId, familyId);
        return new IssuedRefreshToken(token, expiresIn);
    }

    public async Task<RotatedTokens> ValidateAndRotateAsync(string refreshToken, CancellationToken ct = default)
    {
        // 1. Verify chữ ký + lifetime + issuer/audience.
        ClaimsPrincipal principal;
        try
        {
            principal = ValidateToken(refreshToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Refresh token không hợp lệ (chữ ký/hết hạn).");
            throw new UnauthorizedException("Invalid or expired refresh token");
        }

        var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var userIdStr = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                        ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var familyId = principal.FindFirst(FamilyClaim)?.Value;

        if (string.IsNullOrEmpty(jti) || string.IsNullOrEmpty(userIdStr) ||
            string.IsNullOrEmpty(familyId) || !Guid.TryParse(userIdStr, out var userId))
        {
            throw new UnauthorizedException("Malformed refresh token");
        }

        // 2. jti còn trong Redis? Không → token đã xoay/revoke/reuse.
        var stored = await _cache.GetStringAsync(JtiKey(jti), ct);
        if (stored is null)
        {
            // Family còn marker mà jti mất → reuse (token theft) → revoke cả family.
            if (await _cache.GetStringAsync(FamilyKey(familyId), ct) is not null)
            {
                await _cache.RemoveAsync(FamilyKey(familyId), ct);
                _logger.LogWarning(
                    "Phát hiện reuse refresh token — revoke family. UserId={UserId}, Family={Family}, Jti={Jti}",
                    userId, familyId, jti);
            }
            throw new UnauthorizedException("Refresh token has been revoked");
        }

        // 3. Family còn hiệu lực?
        if (await _cache.GetStringAsync(FamilyKey(familyId), ct) is null)
            throw new UnauthorizedException("Refresh token family revoked");

        // 4. User còn tồn tại + active?
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || !user.IsActive)
        {
            await RevokeFamilyAsync(familyId, jti, ct);
            throw new UnauthorizedException("User not found or inactive");
        }

        // 5. Rotation: xoá jti cũ, cấp access + refresh mới (cùng family).
        await _cache.RemoveAsync(JtiKey(jti), ct);
        var (newRefresh, _, refreshExpiresIn) = await CreateAndStoreRefreshTokenAsync(userId, familyId, ct);
        var (accessToken, accessExpiresIn) = GenerateAccessToken(user);

        _logger.LogInformation("Rotated refresh token. UserId={UserId}, Family={Family}", userId, familyId);

        return new RotatedTokens(
            accessToken, accessExpiresIn,
            newRefresh, refreshExpiresIn,
            new UserId(user.Id, user.Email, user.FullName, user.Role.ToString()));
    }

    public async Task RevokeAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(refreshToken)) return;

        ClaimsPrincipal principal;
        try { principal = ValidateToken(refreshToken); }
        catch { return; } // token rác/hết hạn → không cần revoke gì

        var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var familyId = principal.FindFirst(FamilyClaim)?.Value;
        if (!string.IsNullOrEmpty(jti) && !string.IsNullOrEmpty(familyId))
            await RevokeFamilyAsync(familyId, jti, ct);
    }

    // ── helpers ──────────────────────────────────────────────────────

    private async Task RevokeFamilyAsync(string familyId, string jti, CancellationToken ct)
    {
        await _cache.RemoveAsync(JtiKey(jti), ct);
        await _cache.RemoveAsync(FamilyKey(familyId), ct);
    }

    private async Task<(string token, string jti, int expiresIn)> CreateAndStoreRefreshTokenAsync(
        Guid userId, string familyId, CancellationToken ct)
    {
        var jti = Guid.NewGuid().ToString("N");
        var expiresIn = RefreshExpiresInSeconds();
        var token = BuildRefreshJwt(userId, jti, familyId, expiresIn);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(expiresIn)
        };
        await _cache.SetStringAsync(JtiKey(jti), $"{userId}:{familyId}", options, ct);
        await _cache.SetStringAsync(FamilyKey(familyId), "1", options, ct);

        return (token, jti, expiresIn);
    }

    private string BuildRefreshJwt(Guid userId, string jti, string familyId, int expiresIn)
    {
        var (key, issuer, audience) = ReadJwtConfig();
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, jti),
            new Claim(FamilyClaim, familyId)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(expiresIn),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private (string token, int expiresIn) GenerateAccessToken(User user)
    {
        var (key, issuer, audience) = ReadJwtConfig();
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresIn = AccessExpiresInSeconds();

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(expiresIn),
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresIn);
    }

    private ClaimsPrincipal ValidateToken(string token)
    {
        var (key, issuer, audience) = ReadJwtConfig();
        var handler = new JwtSecurityTokenHandler();
        return handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.Zero
        }, out _);
    }

    private (SymmetricSecurityKey key, string issuer, string audience) ReadJwtConfig()
    {
        var jwt = _config.GetSection("Jwt");
        var secret = jwt["Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is not configured");
        if (secret.Length < 32)
            throw new InvalidOperationException("Jwt:Secret must be at least 32 characters");

        return (
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            jwt["Issuer"] ?? "WorkspaceHub",
            jwt["Audience"] ?? "WorkspaceHub");
    }

    private int AccessExpiresInSeconds()
        => int.TryParse(_config["Jwt:ExpiresIn"], out var v) ? v : 3600;

    private int RefreshExpiresInSeconds()
        => int.TryParse(_config["Jwt:RefreshExpiresIn"], out var v) ? v : 60 * 60 * 24 * 7; // 7 ngày

    private static string JtiKey(string jti) => $"refresh:{jti}";
    private static string FamilyKey(string fam) => $"refreshfam:{fam}";
}
