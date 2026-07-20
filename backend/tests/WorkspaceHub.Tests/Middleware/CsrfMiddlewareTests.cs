using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using WorkspaceHub.Api.Auth;
using WorkspaceHub.Api.Middleware;
using Xunit;

namespace WorkspaceHub.Tests.Middleware;

/// <summary>
/// Tests cho <see cref="CsrfMiddleware"/> (SCRUM-62) — double-submit cookie.
/// Chỉ chặn request mutating dùng phiên cookie (<c>wh_access</c>); Bearer, GET,
/// endpoint khởi tạo phiên và SignalR hub được miễn.
/// </summary>
public class CsrfMiddlewareTests
{
    private sealed record CsrfErrorBody(string Error, string Message, string[] Details, string TraceId);

    /// <summary>
    /// Chạy middleware với một request dựng sẵn. Trả về (nextCalled, status, body thô).
    /// </summary>
    private static async Task<(bool nextCalled, int status, string body)> RunAsync(
        string method,
        string path = "/api/items",
        string? cookieHeader = null,
        string? csrfHeader = null,
        string? authorizationHeader = null)
    {
        var nextCalled = false;
        var middleware = new CsrfMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext { TraceIdentifier = "trace-csrf" };
        context.Request.Method = method;
        context.Request.Path = path;
        if (cookieHeader is not null)
            context.Request.Headers["Cookie"] = cookieHeader;
        if (csrfHeader is not null)
            context.Request.Headers[AuthCookieService.CsrfHeaderName] = csrfHeader;
        if (authorizationHeader is not null)
            context.Request.Headers["Authorization"] = authorizationHeader;

        var responseStream = new MemoryStream();
        context.Response.Body = responseStream;

        await middleware.InvokeAsync(context);

        responseStream.Position = 0;
        var body = await new StreamReader(responseStream).ReadToEndAsync();
        return (nextCalled, context.Response.StatusCode, body);
    }

    /// <summary>Cookie header của một phiên cookie hợp lệ (access + csrf).</summary>
    private static string SessionCookies(string csrfValue = "csrf-abc") =>
        $"{AuthCookieService.AccessCookieName}=jwt-token; {AuthCookieService.CsrfCookieName}={csrfValue}";

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task Invoke_MethodKhongMutating_ChoQuaDuThieuCsrf(string method)
    {
        var (nextCalled, status, _) = await RunAsync(method, cookieHeader: SessionCookies());

        nextCalled.Should().BeTrue();
        status.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Invoke_PostKhongCoCookieAccess_ChoQua()
    {
        // Không có cookie phiên → không phải luồng cookie → CSRF không áp dụng.
        var (nextCalled, status, _) = await RunAsync("POST");

        nextCalled.Should().BeTrue();
        status.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Invoke_PostDungBearerHeader_ChoQua()
    {
        var (nextCalled, _, _) = await RunAsync("POST", authorizationHeader: "Bearer eyJhbGciOi...");

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Invoke_PostCoCaBearerVaCookie_BearerThang_ChoQua()
    {
        // Mirror logic OnMessageReceived: có Bearer thì Bearer thắng → bỏ qua CSRF.
        var (nextCalled, _, _) = await RunAsync(
            "POST",
            cookieHeader: SessionCookies(),
            authorizationHeader: "bearer eyJhbGciOi..."); // case-insensitive

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Invoke_PostCookieVaHeaderCsrfKhop_ChoQua()
    {
        var (nextCalled, status, _) = await RunAsync(
            "POST",
            cookieHeader: SessionCookies("token-khop"),
            csrfHeader: "token-khop");

        nextCalled.Should().BeTrue();
        status.Should().Be(StatusCodes.Status200OK);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Invoke_MethodMutatingThieuHeaderCsrf_Tra403(string method)
    {
        var (nextCalled, status, _) = await RunAsync(method, cookieHeader: SessionCookies());

        nextCalled.Should().BeFalse();
        status.Should().Be((int)HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invoke_HeaderCsrfKhongKhopCookie_Tra403()
    {
        var (nextCalled, status, _) = await RunAsync(
            "POST",
            cookieHeader: SessionCookies("cookie-value"),
            csrfHeader: "header-value-khac");

        nextCalled.Should().BeFalse();
        status.Should().Be((int)HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invoke_CoHeaderCsrfNhungThieuCookieCsrf_Tra403()
    {
        var (nextCalled, status, _) = await RunAsync(
            "POST",
            cookieHeader: $"{AuthCookieService.AccessCookieName}=jwt-token",
            csrfHeader: "header-value");

        nextCalled.Should().BeFalse();
        status.Should().Be((int)HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invoke_CsrfKhongHopLe_TraJsonChuanCoErrorCsrfError()
    {
        var (_, status, body) = await RunAsync("POST", cookieHeader: SessionCookies());

        status.Should().Be((int)HttpStatusCode.Forbidden);

        var error = JsonSerializer.Deserialize<CsrfErrorBody>(body, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        })!;

        error.Error.Should().Be("CsrfError");
        error.Message.Should().Be("Missing or invalid CSRF token.");
        error.Details.Should().BeEmpty();
        error.TraceId.Should().Be("trace-csrf");
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/register")]
    [InlineData("/api/auth/google/start")]
    [InlineData("/api/auth/google/callback")]
    [InlineData("/api/auth/refresh")]
    [InlineData("/api/auth/send-otp")]
    [InlineData("/api/auth/verify-otp")]
    [InlineData("/api/internal/process-scheduled")]
    [InlineData("/api/internal/process-sync")]
    [InlineData("/API/Auth/Login")] // exempt so khớp không phân biệt hoa thường
    public async Task Invoke_PathDuocMienTru_ChoQuaDuThieuCsrf(string path)
    {
        var (nextCalled, status, _) = await RunAsync("POST", path, cookieHeader: SessionCookies());

        nextCalled.Should().BeTrue();
        status.Should().Be(StatusCodes.Status200OK);
    }

    [Theory]
    [InlineData("/api/hubs/notifications/negotiate")]
    [InlineData("/api/hubs")]
    public async Task Invoke_PathSignalRHub_ChoQuaDuThieuCsrf(string path)
    {
        var (nextCalled, _, _) = await RunAsync("POST", path, cookieHeader: SessionCookies());

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Invoke_PathChiTrungPrefixVoiExempt_VanBiKiemTra()
    {
        // Exempt dùng exact match nên /api/auth/login-history KHÔNG được miễn.
        var (nextCalled, status, _) = await RunAsync(
            "POST", "/api/auth/login-history", cookieHeader: SessionCookies());

        nextCalled.Should().BeFalse();
        status.Should().Be((int)HttpStatusCode.Forbidden);
    }
}
