namespace WorkspaceHub.Application.OAuth.Core;

/// <summary>
/// Dữ liệu lưu cache phía server cho mỗi OAuth state token.
/// Gộp integrationKey + userId + serviceType để callback xác minh đúng user và service đang connect.
/// ServiceType lưu dạng string (tránh enum deserialization issue khi đọc JSON từ cache).
/// </summary>
public class OAuthStatePayload
{
    public string IntegrationKey { get; init; } = string.Empty;
    public Guid UserId { get; init; }
    public string RedirectUri { get; init; } = string.Empty;
    public string ServiceType { get; init; } = string.Empty;

    public OAuthStatePayload() { }

    public OAuthStatePayload(string integrationKey, Guid userId, string redirectUri, string serviceType)
    {
        IntegrationKey = integrationKey;
        UserId = userId;
        RedirectUri = redirectUri;
        ServiceType = serviceType;
    }
}
