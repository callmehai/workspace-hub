using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Infrastructure.Services;

/// <summary>
/// SMS sender dev fallback (SCRUM-64) — KHÔNG gửi SMS thật, chỉ ghi nội dung ra log
/// để dev/demo đọc được OTP khi chưa cấu hình Twilio (Sms:Twilio:*).
/// </summary>
public class LogSmsSender : ISmsSender
{
    private readonly ILogger<LogSmsSender> _logger;

    public LogSmsSender(ILogger<LogSmsSender> logger) => _logger = logger;

    public Task SendAsync(string toPhoneE164, string message, CancellationToken ct = default)
    {
        _logger.LogWarning("[DEV SMS] To={Phone} | {Message}", toPhoneE164, message);
        return Task.CompletedTask;
    }
}
