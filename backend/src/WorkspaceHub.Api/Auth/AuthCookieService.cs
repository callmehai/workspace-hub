namespace WorkspaceHub.Api.Auth;

/// <summary>
/// Quản lý cookie xác thực (SCRUM-62). Access token JWT lưu trong cookie
/// <c>wh_access</c> (HttpOnly → JS không đọc được, chống XSS đánh cắp token).
/// Kèm cookie <c>wh_csrf</c> (KHÔNG HttpOnly) cho cơ chế double-submit chống CSRF:
/// FE đọc giá trị này gắn vào header <c>X-CSRF-Token</c>, middleware so khớp.
///
/// KHÔNG tự mã hoá token ở đây — JWT đã được ký (HMAC) và HttpOnly cookie là lớp
/// bảo vệ đúng. Mã hoá ở client/response là bảo mật giả (key lộ trong môi trường chạy).
/// </summary>
public class AuthCookieService
{
    public const string AccessCookieName = "wh_access";
    public const string CsrfCookieName = "wh_csrf";
    public const string CsrfHeaderName = "X-CSRF-Token";

    private readonly bool _secure;
    private readonly SameSiteMode _sameSite;

    public AuthCookieService(IConfiguration config, IHostEnvironment env)
    {
        // Prod: cookie cross-site (FE khác origin) cần SameSite=None + Secure.
        // Dev: same-origin qua Vite proxy → Lax + không bắt Secure (http://localhost).
        var crossSite = config.GetValue<bool>("Auth:CrossSiteCookies");
        _sameSite = crossSite ? SameSiteMode.None : SameSiteMode.Lax;
        _secure = crossSite || !env.IsDevelopment();
    }

    /// <summary>Set cookie access token + CSRF token sau khi đăng nhập thành công.</summary>
    public void IssueAccessCookie(HttpResponse response, string accessToken, int expiresInSeconds)
    {
        var expires = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);

        response.Cookies.Append(AccessCookieName, accessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = _secure,
            SameSite = _sameSite,
            Path = "/",
            Expires = expires
        });

        // CSRF token: random, KHÔNG HttpOnly (FE phải đọc được để gắn header).
        response.Cookies.Append(CsrfCookieName, Guid.NewGuid().ToString("N"), new CookieOptions
        {
            HttpOnly = false,
            Secure = _secure,
            SameSite = _sameSite,
            Path = "/",
            Expires = expires
        });
    }

    /// <summary>Xoá cookie auth (logout).</summary>
    public void ClearAuthCookies(HttpResponse response)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = _secure,
            SameSite = _sameSite,
            Path = "/"
        };
        response.Cookies.Delete(AccessCookieName, options);
        response.Cookies.Delete(CsrfCookieName, new CookieOptions
        {
            HttpOnly = false,
            Secure = _secure,
            SameSite = _sameSite,
            Path = "/"
        });
    }
}
