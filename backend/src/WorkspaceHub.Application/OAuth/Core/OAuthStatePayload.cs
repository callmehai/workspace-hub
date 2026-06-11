namespace WorkspaceHub.Application.OAuth.Core;

/// <summary>
/// Dữ liệu lưu cache phía server cho mỗi OAuth state token.
/// Gộp integrationKey + userId để callback xác minh đúng user tạo ra state này.
/// </summary>
public record OAuthStatePayload(string IntegrationKey, Guid UserId, string RedirectUri);
