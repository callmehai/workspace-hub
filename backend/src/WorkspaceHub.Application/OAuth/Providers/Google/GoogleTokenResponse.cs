using System.Text.Json.Serialization;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.OAuth.Providers.Google;

internal class GoogleTokenResponse
{
    [JsonPropertyName("access_token")]  public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    [JsonPropertyName("expires_in")]    public int ExpiresIn { get; set; }
    [JsonPropertyName("scope")]         public string Scope { get; set; } = string.Empty;
    [JsonPropertyName("id_token")]      public string? IdToken { get; set; }
    [JsonPropertyName("token_type")]    public string TokenType { get; set; } = "Bearer";
}


