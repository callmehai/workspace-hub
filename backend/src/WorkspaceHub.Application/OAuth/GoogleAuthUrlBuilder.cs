using System.Web;

namespace WorkspaceHub.Application.OAuth;

public class GoogleAuthUrlBuilder
{
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private readonly string _clientId;
    private readonly string _redirectUri;

    public GoogleAuthUrlBuilder(string clientId, string redirectUri)
    {
        _clientId = clientId;
        _redirectUri = redirectUri;
    }

    public string BuildForLogin(string state)
        => Build(GoogleScopes.Login, state, includeGrantedScopes: false);

    public string BuildForService(string serviceScope, string state)
        => Build(new[] { serviceScope }, state, includeGrantedScopes: true);

    private string Build(IEnumerable<string> scopes, string state, bool includeGrantedScopes)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = _clientId;
        query["redirect_uri"] = _redirectUri;
        query["response_type"] = "code";
        query["scope"] = string.Join(' ', scopes);
        query["state"] = state;
        query["access_type"] = "offline";
        query["prompt"] = "consent";
        if (includeGrantedScopes) query["include_granted_scopes"] = "true";
        return $"{AuthEndpoint}?{query}";
    }
}
