using System.Web;

namespace WorkspaceHub.Application.OAuth.Providers.Google;

internal class GoogleAuthUrlBuilder
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

    // Connect service: thêm "select_account" để user CHỌN được tài khoản Google khác (multi-account) —
    // không có nó, Google tự dùng account đang đăng nhập → không thêm được Gmail thứ 2.
    // Giữ "consent" (đi cùng access_type=offline) để luôn được cấp lại refresh token.
    public string BuildForService(string serviceScope, string state)
        => Build(new[] { serviceScope }, state, includeGrantedScopes: false, prompt: "select_account consent");

    private string Build(IEnumerable<string> scopes, string state, bool includeGrantedScopes, string prompt = "consent")
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = _clientId;
        query["redirect_uri"] = _redirectUri;
        query["response_type"] = "code";
        query["scope"] = string.Join(' ', scopes);
        query["state"] = state;
        query["access_type"] = "offline";
        query["prompt"] = prompt;
        if (includeGrantedScopes) query["include_granted_scopes"] = "true";
        return $"{AuthEndpoint}?{query}";
    }
}
