using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
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
/// </summary>
public class OtpService : IOtpService
{
    private readonly IDistributedCache _cache;
    private readonly ISmsSender _sms;
    private readonly ILogger<OtpService> _logger;

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private const int CooldownSeconds = 60;
    private const int MaxAttempts = 5;

    public OtpService(IDistributedCache cache, ISmsSender sms, ILogger<OtpService> logger)
    {
        _cache = cache;
        _sms = sms;
        _logger = logger;
    }

    public async Task<int> SendAsync(Guid userId, string phoneE164, CancellationToken ct = default)
    {
        // Cooldown: chặn spam gửi lại.
        if (await _cache.GetStringAsync(CooldownKey(userId), ct) is not null)
            throw new BusinessRuleException($"Vui lòng đợi {CooldownSeconds}s trước khi gửi lại mã.");

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var hash = Hash(userId, code);
        var expiry = DateTimeOffset.UtcNow.Add(Ttl);

        await _cache.SetStringAsync(OtpKey(userId), $"{hash}:0:{expiry.UtcTicks}",
            new DistributedCacheEntryOptions { AbsoluteExpiration = expiry }, ct);
        await _cache.SetStringAsync(CooldownKey(userId), "1",
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(CooldownSeconds) }, ct);

        await _sms.SendAsync(phoneE164, $"Mã xác minh Workspace Hub của bạn là: {code} (hết hạn sau 5 phút).", ct);
        _logger.LogInformation("Đã gửi OTP. UserId={UserId}", userId);

        return CooldownSeconds;
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
