using System.Diagnostics;
using System.Security.Claims;

namespace WorkspaceHub.Api.Middleware;

/// <summary>
/// Structured logging cho mỗi HTTP request (SCRUM-25): ghi 1 dòng "completion" ở mức phù hợp
/// (Information cho 2xx/3xx, Warning cho ≥400) kèm method, path, status code,
/// thời gian xử lý (ms) và userId (nếu đã authenticate).
///
/// Đặt OUTERMOST (trước <see cref="ExceptionMiddleware"/>) để:
///   • đo trọn thời gian toàn pipeline — kể cả lúc ExceptionMiddleware xử lý lỗi;
///   • đọc đúng status code cuối cùng. Với 5xx, ExceptionMiddleware đã nuốt exception và
///     set status code, nên ở đây <c>_next</c> trả về bình thường (không nhận lại exception)
///     và đọc được status 5xx chuẩn.
///
/// Chi tiết lỗi 5xx (stack trace / traceId) do ExceptionMiddleware log ở mức Error — middleware
/// này chỉ log dòng completion ở mức Warning cho ≥400, tránh log Error trùng.
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            var statusCode = context.Response.StatusCode;

            // Mirror cách ApiControllerBase lấy UserId: NameIdentifier rồi fallback "sub".
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? context.User.FindFirstValue("sub")
                         ?? "anonymous";

            var level = statusCode >= 400 ? LogLevel.Warning : LogLevel.Information;

            // Placeholder có tên → tự thành structured property khi dùng provider hỗ trợ.
            _logger.Log(level,
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {ElapsedMs}ms (user {UserId})",
                context.Request.Method,
                context.Request.Path.Value,
                statusCode,
                stopwatch.ElapsedMilliseconds,
                userId);
        }
    }
}
