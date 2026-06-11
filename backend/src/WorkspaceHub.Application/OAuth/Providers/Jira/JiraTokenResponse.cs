using System.Text.Json.Serialization;

namespace WorkspaceHub.Application.OAuth.Providers.Jira;

internal class JiraTokenResponse
{
    [JsonPropertyName("access_token")]  public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    [JsonPropertyName("expires_in")]    public int ExpiresIn { get; set; }
    [JsonPropertyName("scope")]         public string Scope { get; set; } = string.Empty;
    [JsonPropertyName("token_type")]    public string TokenType { get; set; } = "Bearer";
}
