using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Infrastructure.Services;

/// <summary>
/// Gửi SMS qua Twilio REST API (SCRUM-64) — gọi trực tiếp endpoint Messages, không cần SDK.
/// Đọc credential từ config Sms:Twilio:AccountSid/AuthToken/FromNumber.
/// </summary>
public class TwilioSmsSender : ISmsSender
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<TwilioSmsSender> _logger;

    public TwilioSmsSender(IHttpClientFactory httpFactory, IConfiguration config, ILogger<TwilioSmsSender> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(string toPhoneE164, string message, CancellationToken ct = default)
    {
        var accountSid = _config["Sms:Twilio:AccountSid"];
        var authToken = _config["Sms:Twilio:AuthToken"];
        var fromNumber = _config["Sms:Twilio:FromNumber"];

        if (string.IsNullOrWhiteSpace(accountSid) || string.IsNullOrWhiteSpace(authToken) ||
            string.IsNullOrWhiteSpace(fromNumber))
            throw new InvalidOperationException("Sms:Twilio:AccountSid/AuthToken/FromNumber chưa cấu hình.");

        var client = _httpFactory.CreateClient("Twilio");
        var url = $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json";

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = toPhoneE164,
                ["From"] = fromNumber,
                ["Body"] = message
            })
        };
        // Basic auth: AccountSid:AuthToken
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{accountSid}:{authToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Twilio SendAsync network error. To={Phone}", toPhoneE164);
            throw new ProviderException("Không gửi được SMS (Twilio không phản hồi).");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Twilio gửi SMS thất bại. Status={Status}, Body={Body}", response.StatusCode, body);
            throw new ProviderException("Gửi SMS thất bại (Twilio trả lỗi).");
        }

        _logger.LogInformation("Đã gửi SMS qua Twilio. To={Phone}", toPhoneE164);
    }
}
