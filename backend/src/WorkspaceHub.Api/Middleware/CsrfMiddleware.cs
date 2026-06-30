using System.Net;
using System.Text.Json;
using WorkspaceHub.Api.Auth;

namespace WorkspaceHub.Api.Middleware;

/// <summary>
/// Chống CSRF bằng double-submit cookie (SCRUM-62). Vì access token nằm trong cookie
/// (tự gửi kèm mọi request), request mutating phải kèm header <c>X-CSRF-Token</c> khớp
/// với cookie <c>wh_csrf</c> — attacker cross-site không đọc được cookie nên không giả
/// được header.
///
/// Chỉ kiểm tra method mutating (POST/PUT/PATCH/DELETE). Bỏ qua:
///   - Request KHÔNG có cookie access (dùng Bearer header — Swagger/Postman/máy chủ-máy chủ;
///     Bearer không bị CSRF vì attacker không có token để gắn header).
///   - Endpoint khởi tạo phiên (login/register/google/refresh) — chưa có cookie CSRF lúc đó.
/// </summary>
public class CsrfMiddleware
{
    private readonly RequestDelegate _next;

    // Endpoint thiết lập phiên — chưa có cookie CSRF khi gọi lần đầu nên miễn kiểm tra.
    private static readonly string[] ExemptPaths =
    {
        "/api/auth/login",
        "/api/auth/register",
        "/api/auth/google/start",
        "/api/auth/google/callback",
        "/api/auth/refresh",
        "/api/auth/send-otp",
        "/api/auth/verify-otp",
        "/api/internal/process-scheduled" // bảo vệ riêng bằng X-Cron-Secret
    };

    private static readonly HashSet<string> MutatingMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    public CsrfMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresCheck(context) && !IsValid(context))
        {
            await WriteForbiddenAsync(context);
            return;
        }

        await _next(context);
    }

    private static bool RequiresCheck(HttpContext context)
    {
        if (!MutatingMethods.Contains(context.Request.Method))
            return false;

        // Không có cookie access → không phải phiên cookie (Bearer) → không cần CSRF.
        if (!context.Request.Cookies.ContainsKey(AuthCookieService.AccessCookieName))
            return false;

        var path = context.Request.Path.Value ?? string.Empty;
        // Exact match (không StartsWith) — tránh exempt nhầm endpoint tương lai như
        // /api/auth/login-history vô tình khớp prefix /api/auth/login.
        return !ExemptPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsValid(HttpContext context)
    {
        var cookieToken = context.Request.Cookies[AuthCookieService.CsrfCookieName];
        var headerToken = context.Request.Headers[AuthCookieService.CsrfHeaderName].ToString();

        return !string.IsNullOrEmpty(cookieToken)
               && !string.IsNullOrEmpty(headerToken)
               && cookieToken == headerToken;
    }

    private static async Task WriteForbiddenAsync(HttpContext context)
    {
        context.Response.Clear();
        context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
        context.Response.ContentType = "application/json";

        var body = new
        {
            error = "CsrfError",
            message = "Missing or invalid CSRF token.",
            details = Array.Empty<string>(),
            traceId = context.TraceIdentifier
        };
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
