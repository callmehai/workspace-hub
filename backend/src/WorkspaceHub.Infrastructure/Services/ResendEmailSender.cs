using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Infrastructure.Services;

/// <summary>
/// Gửi email hệ thống qua Resend REST API (SCRUM-64) — gọi trực tiếp endpoint /emails, không cần SDK.
/// Đọc credential từ config Email:Resend:ApiKey + Email:Resend:FromAddress.
///
/// ⚠️ Sender HỆ THỐNG, KHÔNG phải Gmail của user (xem <see cref="ISystemEmailSender"/>).
/// Test mode Resend (chưa verify domain): FromAddress phải là onboarding@resend.dev và
/// chỉ gửi được tới email chủ tài khoản Resend — verify domain để gửi tự do.
/// </summary>
public class ResendEmailSender : ISystemEmailSender
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ResendEmailSender> _logger;

    // Config đọc 1 lần ở ctor — chỉ resolve khi đã chọn Resend (có ApiKey).
    private readonly string _apiKey;
    private readonly string _fromAddress;

    private const string ResendEndpoint = "https://api.resend.com/emails";

    public ResendEmailSender(IHttpClientFactory httpFactory, IConfiguration config, ILogger<ResendEmailSender> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;

        _apiKey = config["Email:Resend:ApiKey"]
            ?? throw new InvalidOperationException("Email:Resend:ApiKey chưa cấu hình.");
        _fromAddress = config["Email:Resend:FromAddress"]
            ?? throw new InvalidOperationException("Email:Resend:FromAddress chưa cấu hình.");
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken ct = default)
    {
        var client = _httpFactory.CreateClient("Resend");

        var request = new HttpRequestMessage(HttpMethod.Post, ResendEndpoint)
        {
            Content = JsonContent.Create(new
            {
                from = _fromAddress,
                to = new[] { toEmail },
                subject,
                html = htmlBody,
                text = textBody
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Resend SendAsync network error. To={Email}", toEmail);
            throw new ProviderException("Không gửi được email (Resend không phản hồi).");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Resend gửi email thất bại. Status={Status}, Body={Body}", response.StatusCode, body);
            throw new ProviderException("Gửi email thất bại (Resend trả lỗi).");
        }

        _logger.LogInformation("Đã gửi email qua Resend. To={Email}", toEmail);
    }
}
