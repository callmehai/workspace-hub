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
