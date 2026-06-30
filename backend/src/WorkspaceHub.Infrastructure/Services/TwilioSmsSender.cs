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
    private readonly ILogger<TwilioSmsSender> _logger;

    // Config đọc 1 lần ở ctor (review #5) — chỉ resolve khi đã chọn Twilio (có AccountSid).
    private readonly string _accountSid;
    private readonly string _authToken;
    private readonly string _fromNumber;

    public TwilioSmsSender(IHttpClientFactory httpFactory, IConfiguration config, ILogger<TwilioSmsSender> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;

        _accountSid = config["Sms:Twilio:AccountSid"]
            ?? throw new InvalidOperationException("Sms:Twilio:AccountSid chưa cấu hình.");
        _authToken = config["Sms:Twilio:AuthToken"]
            ?? throw new InvalidOperationException("Sms:Twilio:AuthToken chưa cấu hình.");
        _fromNumber = config["Sms:Twilio:FromNumber"]
            ?? throw new InvalidOperationException("Sms:Twilio:FromNumber chưa cấu hình.");
    }

    public async Task SendAsync(string toPhoneE164, string message, CancellationToken ct = default)
    {
        var client = _httpFactory.CreateClient("Twilio");
        var url = $"https://api.twilio.com/2010-04-01/Accounts/{_accountSid}/Messages.json";

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = toPhoneE164,
                ["From"] = _fromNumber,
                ["Body"] = message
            })
        };
        // Basic auth: AccountSid:AuthToken
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_accountSid}:{_authToken}"));
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
