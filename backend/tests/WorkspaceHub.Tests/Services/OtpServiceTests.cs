using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Infrastructure.Services;
using Xunit;

namespace WorkspaceHub.Tests.Services;

/// <summary>SCRUM-64 — OtpService: send/verify, cooldown, max-attempts, expiry.</summary>
public class OtpServiceTests
{
    private static IDistributedCache CreateCache()
        => new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    /// <summary>SMS sender bắt OTP gửi đi để test verify với đúng mã.</summary>
    private sealed class CapturingSms : ISmsSender
    {
        public string? LastMessage { get; private set; }
        public Task SendAsync(string toPhoneE164, string message, CancellationToken ct = default)
        {
            LastMessage = message;
            return Task.CompletedTask;
        }
    }

    private static string ExtractCode(string message)
        => System.Text.RegularExpressions.Regex.Match(message, @"\d{6}").Value;

    private static (OtpService Service, CapturingSms Sms) Create()
    {
        var sms = new CapturingSms();
        var service = new OtpService(CreateCache(), sms, NullLogger<OtpService>.Instance);
        return (service, sms);
    }

    /// <summary>
    /// Cache spy: ghi lại AbsoluteExpiration của mỗi lần Set theo key — để test verify rằng
    /// nhập sai KHÔNG gia hạn cửa sổ TTL của OTP (regression guard cho bug review #1).
    /// </summary>
    private sealed class ExpiryCapturingCache : IDistributedCache
    {
        private readonly IDistributedCache _inner = CreateCache();
        public readonly Dictionary<string, DateTimeOffset?> LastAbsoluteExpiration = new();

        public byte[]? Get(string key) => _inner.Get(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => _inner.GetAsync(key, token);
        public void Refresh(string key) => _inner.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);
        public void Remove(string key) => _inner.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(key, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            LastAbsoluteExpiration[key] = options.AbsoluteExpiration;
            _inner.Set(key, value, options);
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            LastAbsoluteExpiration[key] = options.AbsoluteExpiration;
            return _inner.SetAsync(key, value, options, token);
        }
    }

    [Fact]
    public async Task Wrong_attempt_does_not_extend_ttl()
    {
        var userId = Guid.NewGuid();
        var cache = new ExpiryCapturingCache();
        var sms = new CapturingSms();
        var service = new OtpService(cache, sms, NullLogger<OtpService>.Instance);

        await service.SendAsync(userId, "+84901234567");
        var otpKey = $"otp:{userId}";
        var expiryAfterSend = cache.LastAbsoluteExpiration[otpKey];

        // Nhập sai → entry được ghi lại (tăng attempts) nhưng phải GIỮ NGUYÊN absolute expiry.
        var ok = await service.VerifyAsync(userId, "000000");
        ok.Should().BeFalse();
        var expiryAfterWrong = cache.LastAbsoluteExpiration[otpKey];

        expiryAfterWrong.Should().Be(expiryAfterSend,
            "lần nhập sai không được gia hạn cửa sổ tấn công của OTP");
    }

    [Fact]
    public async Task Send_then_verify_correct_code_succeeds()
    {
        var userId = Guid.NewGuid();
        var (service, sms) = Create();

        var cooldown = await service.SendAsync(userId, "+84901234567");
        cooldown.Should().Be(60);

        var code = ExtractCode(sms.LastMessage!);
        var ok = await service.VerifyAsync(userId, code);

        ok.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_wrong_code_returns_false()
    {
        var userId = Guid.NewGuid();
        var (service, _) = Create();
        await service.SendAsync(userId, "+84901234567");

        var ok = await service.VerifyAsync(userId, "000000");

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task Resend_within_cooldown_throws()
    {
        var userId = Guid.NewGuid();
        var (service, _) = Create();
        await service.SendAsync(userId, "+84901234567");

        var act = () => service.SendAsync(userId, "+84901234567");

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Verify_without_send_throws()
    {
        var (service, _) = Create();

        var act = () => service.VerifyAsync(Guid.NewGuid(), "123456");

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Too_many_wrong_attempts_invalidates_otp()
    {
        var userId = Guid.NewGuid();
        var (service, _) = Create();
        await service.SendAsync(userId, "+84901234567");

        // 5 lần sai → lần thứ 5 ném (hết lượt) + OTP bị xoá.
        for (var i = 0; i < 4; i++)
            (await service.VerifyAsync(userId, "000000")).Should().BeFalse();

        var fifth = () => service.VerifyAsync(userId, "000000");
        await fifth.Should().ThrowAsync<BusinessRuleException>();

        // Sau khi vô hiệu, verify tiếp (kể cả đúng cũng không còn) → throw "hết hạn".
        var after = () => service.VerifyAsync(userId, "123456");
        await after.Should().ThrowAsync<BusinessRuleException>();
    }
}
