using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Infrastructure.Services;

/// <summary>
/// SCRUM-63 — refresh token service (JWT + jti, danh sách hiệu lực ở Redis qua IDistributedCache).
///
/// Khoá Redis:
///   refresh:{jti}    → "1"   (1 refresh token còn sống; chỉ cần tồn-tại/không, claim đã ký trong JWT)
///   refreshfam:{fam} → "1"   (marker family; xoá = revoke cả family)
///
/// Rotation: mỗi lần refresh cấp jti mới (cùng family) + xoá jti cũ.
/// Reuse detection: token ký hợp lệ + chưa hết hạn nhưng jti KHÔNG còn trong Redis
///   → coi là token đã bị dùng lại (theft) → revoke cả family (xoá marker).
///
/// Chống TOCTOU (SCRUM-63 review): khi có Redis thật, "tiêu thụ" jti bằng thao tác
/// ATOMIC GETDEL (Lua) — 2 request đồng thời cùng token thì chỉ 1 cái xoá được key,
/// cái còn lại thấy null → bị từ chối. Dev fallback (in-memory, không có
/// IConnectionMultiplexer) dùng get+remove không atomic (chấp nhận cho single-instance dev).
/// </summary>
public class RefreshTokenService : IRefreshTokenService
{
    private readonly IDistributedCache _cache;
    private readonly IUserRepository _users;
    private readonly ILogger<RefreshTokenService> _logger;
    private readonly IConnectionMultiplexer? _redis;
    private readonly IJwtTokenFactory _jwt; // nguồn duy nhất sinh access token (review #3)

    private const string FamilyClaim = "fam";

    /// <summary>
    /// Prefix Redis của IDistributedCache (phải KHỚP AddStackExchangeRedisCache InstanceName ở DI).
    /// Đặt 1 chỗ vì thao tác GETDEL atomic gọi thẳng Redis nên cần key đầy đủ (gồm prefix).
    /// </summary>
    public const string RedisInstanceName = "wh:";

    // JWT config đọc 1 lần (review #4) — config không đổi trong vòng đời service (Scoped).
    // Chỉ dùng cho refresh JWT (build + validate); access token do IJwtTokenFactory sinh.
    private readonly SymmetricSecurityKey _signingKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _refreshTtl;

    // Dùng EXISTS + DEL vì IDistributedCache (Redis) lưu dưới dạng Hash, gọi GET trực tiếp sẽ bị lỗi WRONGTYPE.
    private const string GetDelScript = "local ex = redis.call('EXISTS', KEYS[1]); if ex == 1 then redis.call('DEL', KEYS[1]); return 1; else return nil; end";

    public RefreshTokenService(
        IConfiguration config,
        IDistributedCache cache,
        IUserRepository users,
        ILogger<RefreshTokenService> logger,
        IJwtTokenFactory jwtFactory,
        IConnectionMultiplexer? redis = null)
    {
        _cache = cache;
        _users = users;
        _logger = logger;
        _jwt = jwtFactory;
        _redis = redis;

        var jwt = config.GetSection("Jwt");
        var secret = jwt["Secret"] ?? throw new InvalidOperationException("Jwt:Secret is not configured");
        if (secret.Length < 32)
            throw new InvalidOperationException("Jwt:Secret must be at least 32 characters");

        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        _issuer = jwt["Issuer"] ?? "WorkspaceHub";
        _audience = jwt["Audience"] ?? "WorkspaceHub";
        _refreshTtl = int.TryParse(jwt["RefreshExpiresIn"], out var r) ? r : 60 * 60 * 24 * 7; // 7 ngày
    }

    public async Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken ct = default)
    {
        var familyId = Guid.NewGuid().ToString("N");
        var (token, _) = await CreateAndStoreRefreshTokenAsync(userId, familyId, ct);
        _logger.LogInformation("Issued refresh token. UserId={UserId}, Family={Family}", userId, familyId);
        return new IssuedRefreshToken(token, _refreshTtl);
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

        // 2. Tiêu thụ jti ATOMIC (chống TOCTOU). consumed=false → jti không còn → reuse/revoke.
        var consumed = await ConsumeJtiAsync(jti, ct);
        if (!consumed)
        {
            // Family còn marker mà jti đã mất → reuse (token theft) → revoke cả family.
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
            await _cache.RemoveAsync(FamilyKey(familyId), ct); // revoke family (jti đã tiêu thụ ở B2)
            throw new UnauthorizedException("User not found or inactive");
        }

        // 5. Rotation: cấp access + refresh mới (cùng family). jti cũ đã bị xoá atomic ở bước 2.
        var (newRefresh, _) = await CreateAndStoreRefreshTokenAsync(userId, familyId, ct);
        var (accessToken, accessExpiresIn) = _jwt.CreateAccessToken(user);

        _logger.LogInformation("Rotated refresh token. UserId={UserId}, Family={Family}", userId, familyId);

        return new RotatedTokens(
            accessToken, accessExpiresIn,
            newRefresh, _refreshTtl,
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
        if (!string.IsNullOrEmpty(jti))
            await _cache.RemoveAsync(JtiKey(jti), ct);
        if (!string.IsNullOrEmpty(familyId))
            await _cache.RemoveAsync(FamilyKey(familyId), ct);
    }

    // ── helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// "Tiêu thụ" jti: trả true nếu key tồn tại VÀ đã xoá thành công (chỉ 1 caller thắng).
    /// Redis thật → GETDEL atomic; in-memory dev → get+remove (best-effort, single-instance).
    /// </summary>
    private async Task<bool> ConsumeJtiAsync(string jti, CancellationToken ct)
    {
        if (_redis is not null)
        {
            var db = _redis.GetDatabase();
            // IDistributedCache thêm InstanceName vào trước key → key thật có prefix.
            var fullKey = $"{RedisInstanceName}{JtiKey(jti)}";
            var result = await db.ScriptEvaluateAsync(GetDelScript, new RedisKey[] { fullKey });
            return !result.IsNull;
        }

        // Fallback dev (in-memory): không atomic — chấp nhận cho single-instance.
        var stored = await _cache.GetStringAsync(JtiKey(jti), ct);
        if (stored is null) return false;
        await _cache.RemoveAsync(JtiKey(jti), ct);
        return true;
    }

    private async Task<(string token, string jti)> CreateAndStoreRefreshTokenAsync(
        Guid userId, string familyId, CancellationToken ct)
    {
        var jti = Guid.NewGuid().ToString("N");
        var token = BuildRefreshJwt(userId, jti, familyId, _refreshTtl);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_refreshTtl)
        };
        // Value chỉ cần "tồn tại/không" — userId/family đã ký trong JWT (review #5).
        await _cache.SetStringAsync(JtiKey(jti), "1", options, ct);
        // Family TTL được "trượt" (gia hạn) mỗi lần rotate: session đang hoạt động giữ family
        // sống tối đa thêm refreshTtl kể từ lần dùng cuối → idle quá refreshTtl thì family chết.
        // (Khác jti dùng absolute expiry.) Đây là chủ ý — sliding session. (review #2)
        await _cache.SetStringAsync(FamilyKey(familyId), "1", options, ct);

        return (token, jti);
    }

    private string BuildRefreshJwt(Guid userId, string jti, string familyId, int expiresIn)
    {
        var creds = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, jti),
            new Claim(FamilyClaim, familyId)
        };
        var token = new JwtSecurityToken(
            issuer: _issuer, audience: _audience, claims: claims,
            expires: DateTime.UtcNow.AddSeconds(expiresIn), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private ClaimsPrincipal ValidateToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        return handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _issuer,
            ValidAudience = _audience,
            IssuerSigningKey = _signingKey,
            ClockSkew = TimeSpan.Zero
        }, out _);
    }

    private static string JtiKey(string jti) => $"refresh:{jti}";
    private static string FamilyKey(string fam) => $"refreshfam:{fam}";
}
