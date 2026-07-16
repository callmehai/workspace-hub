using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Infrastructure.Services;

/// <summary>
/// Email sender dev fallback (SCRUM-64) — KHÔNG gửi email thật, chỉ ghi nội dung ra log
/// để dev/demo đọc được OTP khi chưa cấu hình Resend (Email:Resend:*).
///
/// Ghi <c>textBody</c> (plain text) chứ KHÔNG ghi htmlBody — mã OTP nằm gọn trong text,
/// không bị lẫn trong đống thẻ HTML.
/// </summary>
public class LogEmailSender : ISystemEmailSender
{
    private readonly ILogger<LogEmailSender> _logger;

    public LogEmailSender(ILogger<LogEmailSender> logger) => _logger = logger;

    public Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken ct = default)
    {
        _logger.LogWarning("[DEV EMAIL] To={Email} | Subject={Subject} | {Text}", toEmail, subject, textBody);
        return Task.CompletedTask;
    }
}
