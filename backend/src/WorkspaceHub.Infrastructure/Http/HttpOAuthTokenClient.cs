using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Infrastructure.Http;

public class HttpOAuthTokenClient : IOAuthTokenClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HttpOAuthTokenClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string> PostFormAsync(string tokenEndpoint, Dictionary<string, string> formData, CancellationToken ct = default)
    {
        var http = _httpClientFactory.CreateClient("OAuthToken");
        // Caller quyết định cancellation (luồng OAuth callback truyền CancellationToken.None vì
        // code dùng-một-lần, không được huỷ giữa chừng). HttpClient.Timeout=30s (DI) làm chặn an toàn.
        var response = await http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(formData), ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
