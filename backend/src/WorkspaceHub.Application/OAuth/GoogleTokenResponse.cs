using System.Text.Json.Serialization;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.OAuth;

public class GoogleTokenResponse
{
    [JsonPropertyName("access_token")]  public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    [JsonPropertyName("expires_in")]    public int ExpiresIn { get; set; }
    [JsonPropertyName("scope")]         public string Scope { get; set; } = string.Empty;
    [JsonPropertyName("id_token")]      public string? IdToken { get; set; }
    [JsonPropertyName("token_type")]    public string TokenType { get; set; } = "Bearer";
}

public static class ServiceConnectionSync
{
    public static void ApplyGrantedScopes(OAuthConnection connection, string grantedScopes)
    {
        var grantedServices = GoogleScopes.ServicesFromGrantedScopes(grantedScopes);

        // Đồng bộ 2 chiều: tắt service bị thu hồi, bật service được cấp lại.
        // Xử lý trường hợp user uncheck một số quyền khi Google fine-grained consent.
        foreach (var existing in connection.ServiceConnections)
            existing.IsEnabled = grantedServices.Contains(existing.ServiceType);

        // Thêm mới các service chưa tồn tại trong connection.
        foreach (var service in grantedServices)
        {
            if (!connection.ServiceConnections.Any(sc => sc.ServiceType == service))
                connection.ServiceConnections.Add(new ServiceConnection
                {
                    Id = Guid.NewGuid(),
                    OAuthConnectionId = connection.Id,
                    ServiceType = service,
                    IsEnabled = true,
                    CursorValue = null
                });
        }
    }
}
