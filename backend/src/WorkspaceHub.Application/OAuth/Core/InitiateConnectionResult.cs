namespace WorkspaceHub.Application.OAuth.Core;

/// <summary>Kết quả build auth URL — URL redirect + state CSRF. Dùng chung cho mọi provider.</summary>
public class InitiateConnectionResult
{
    public string AuthorizationUrl { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;

    public InitiateConnectionResult() { }

    public InitiateConnectionResult(string authorizationUrl, string state)
    {
        AuthorizationUrl = authorizationUrl;
        State = state;
    }
}
