namespace WorkspaceHub.Application.OAuth.Core;

/// <summary>
/// Dữ liệu lưu cache phía server cho mỗi OAuth state token.
/// Gộp integrationKey + userId + serviceType để callback xác minh đúng user và service đang connect.
/// ServiceType lưu dạng string (tránh enum deserialization issue khi đọc JSON từ cache).
/// </summary>
public record OAuthStatePayload(string IntegrationKey, Guid UserId, string RedirectUri, string ServiceType);
