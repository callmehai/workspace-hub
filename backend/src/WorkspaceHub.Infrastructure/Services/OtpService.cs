using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Infrastructure.Services;

/// <summary>
/// OTP service (SCRUM-64) — Redis (IDistributedCache) lưu HASH mã 6 số + số lần verify sai.
///
/// Khoá Redis:
///   otp:{userId}          → "{hash}:{attempts}:{expiryTicks}"  (TTL 5')
///   otp:cooldown:{userId} → "1"                                  (TTL 60s — chặn gửi lại quá nhanh)
///
/// Lưu HMAC-SHA256(code, key=userId) thay vì mã thô — Redis lộ cũng không suy ra OTP
/// (và keyed theo userId nên không build được rainbow table dùng chung).
///
/// Chống TOCTOU (review #2): cooldown được "đặt chỗ" ATOMIC bằng SET NX (Lua) khi có Redis
/// thật — 2 request đồng thời cùng userId thì chỉ 1 cái set được key, cái còn lại bị từ chối
/// → đúng 1 email. Dev fallback (in-memory) dùng get+set không atomic (chấp nhận single-instance).
/// </summary>
public class OtpService : IOtpService
{
    private readonly IDistributedCache _cache;
    private readonly ISystemEmailSender _email;
    private readonly ILogger<OtpService> _logger;
    private readonly IConnectionMultiplexer? _redis;

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private const int CooldownSeconds = 60;
    private const int MaxAttempts = 5;

    // SET key value NX EX ttl: chỉ set nếu chưa tồn tại; trả "OK" khi đặt được, nil nếu đã có.
    private const string SetCooldownNxScript =
        "return redis.call('SET', KEYS[1], '1', 'NX', 'EX', ARGV[1])";

    public OtpService(
        IDistributedCache cache,
        ISystemEmailSender email,
        ILogger<OtpService> logger,
        IConnectionMultiplexer? redis = null)
    {
        _cache = cache;
        _email = email;
        _logger = logger;
        _redis = redis;
    }

    public async Task<int> SendAsync(Guid userId, string email, CancellationToken ct = default)
    {
        // Cooldown: chặn spam gửi lại. Đặt chỗ ATOMIC → false nghĩa là đang trong cooldown.
        if (!await TryAcquireCooldownAsync(userId, ct))
            throw new BusinessRuleException($"Vui lòng đợi {CooldownSeconds}s trước khi gửi lại mã.");

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var hash = Hash(userId, code);
        var expiry = DateTimeOffset.UtcNow.Add(Ttl);

        await _cache.SetStringAsync(OtpKey(userId), $"{hash}:0:{expiry.UtcTicks}",
            new DistributedCacheEntryOptions { AbsoluteExpiration = expiry }, ct);

        var (subject, html, text) = BuildOtpEmail(code);
        await _email.SendAsync(email, subject, html, text, ct);
        _logger.LogInformation("Đã gửi OTP. UserId={UserId}", userId);

        return CooldownSeconds;
    }

    /// <summary>Nội dung email OTP: text plain (dev log đọc mã dễ) + html tối giản.</summary>
    private static (string Subject, string Html, string Text) BuildOtpEmail(string code)
    {
        const string subject = "Mã xác minh Workspace Hub";
        var text = $"Mã xác minh Workspace Hub của bạn là: {code}\nMã hết hạn sau 5 phút. Nếu bạn không yêu cầu, hãy bỏ qua email này.";
        var html =
            "<div style=\"font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;max-width:480px;margin:auto\">" +
            "<h2 style=\"margin:0 0 12px\">Xác minh Workspace Hub</h2>" +
            "<p style=\"margin:0 0 16px;color:#334155\">Nhập mã dưới đây để hoàn tất đăng ký:</p>" +
            $"<p style=\"font-size:32px;font-weight:700;letter-spacing:8px;margin:0 0 16px\">{code}</p>" +
            "<p style=\"margin:0;color:#64748b;font-size:13px\">Mã hết hạn sau 5 phút. Nếu bạn không yêu cầu, hãy bỏ qua email này.</p>" +
            "</div>";
        return (subject, html, text);
    }

    /// <summary>
    /// "Đặt chỗ" cooldown: true nếu set được (chưa trong cooldown), false nếu đã có.
    /// Redis thật → SET NX atomic (chống TOCTOU 2 request đồng thời); in-memory dev → get+set.
    /// </summary>
    private async Task<bool> TryAcquireCooldownAsync(Guid userId, CancellationToken ct)
    {
        if (_redis is not null)
        {
            var db = _redis.GetDatabase();
            // IDistributedCache thêm InstanceName vào trước key → key thật có prefix.
            var fullKey = $"{RefreshTokenService.RedisInstanceName}{CooldownKey(userId)}";
            var result = await db.ScriptEvaluateAsync(
                SetCooldownNxScript, new RedisKey[] { fullKey }, new RedisValue[] { CooldownSeconds });
            return !result.IsNull; // "OK" → set được; nil → đã tồn tại.
        }

        // Fallback dev (in-memory): không atomic — chấp nhận cho single-instance.
        if (await _cache.GetStringAsync(CooldownKey(userId), ct) is not null)
            return false;
        await _cache.SetStringAsync(CooldownKey(userId), "1",
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(CooldownSeconds) }, ct);
        return true;
    }

    public async Task<bool> VerifyAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var stored = await _cache.GetStringAsync(OtpKey(userId), ct);
        if (stored is null)
            throw new BusinessRuleException("Mã OTP đã hết hạn hoặc chưa được gửi. Vui lòng gửi lại.");

        var parts = stored.Split(':');
        var storedHash = parts[0];
        var attempts = parts.Length > 1 && int.TryParse(parts[1], out var a) ? a : 0;
        var expiry = parts.Length > 2 && long.TryParse(parts[2], out var t)
            ? new DateTimeOffset(t, TimeSpan.Zero)
            : DateTimeOffset.UtcNow.Add(Ttl); // fallback nếu thiếu (entry cũ)

        if (FixedTimeEquals(Hash(userId, code), storedHash))
        {
            await _cache.RemoveAsync(OtpKey(userId), ct);
            _logger.LogInformation("OTP verify thành công. UserId={UserId}", userId);
            return true;
        }

        attempts++;
        if (attempts >= MaxAttempts)
        {
            await _cache.RemoveAsync(OtpKey(userId), ct); // hết lượt → buộc gửi lại
            _logger.LogWarning("OTP sai quá {Max} lần → vô hiệu. UserId={UserId}", MaxAttempts, userId);
            throw new BusinessRuleException("Nhập sai quá số lần cho phép. Vui lòng gửi lại mã mới.");
        }

        // Cập nhật đếm nhưng GIỮ NGUYÊN absolute expiry — không gia hạn cửa sổ tấn công (review #1).
        var remaining = expiry - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            await _cache.RemoveAsync(OtpKey(userId), ct);
            throw new BusinessRuleException("Mã OTP đã hết hạn. Vui lòng gửi lại.");
        }
        await _cache.SetStringAsync(OtpKey(userId), $"{storedHash}:{attempts}:{expiry.UtcTicks}",
            new DistributedCacheEntryOptions { AbsoluteExpiration = expiry }, ct);
        return false;
    }

    /// <summary>HMAC-SHA256(code) keyed theo userId → không rainbow-table được chung.</summary>
    private static string Hash(Guid userId, string code)
    {
        var key = Encoding.UTF8.GetBytes(userId.ToString("N"));
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(code)));
    }

    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string OtpKey(Guid userId) => $"otp:{userId}";
    private static string CooldownKey(Guid userId) => $"otp:cooldown:{userId}";
}
