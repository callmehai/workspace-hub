using System.Net;
using System.Text.Json;
using FluentValidation;
using WorkspaceHub.Application.Common;

namespace WorkspaceHub.Api.Middleware;

/// <summary>
/// Middleware tập trung bắt mọi exception chưa xử lý → trả error response thống nhất
/// theo format chuẩn (SCRUM-24): <c>{ error, message, details[], traceId }</c>.
///
/// Mapping status code:
///   ValidationException (FluentValidation) → 400
///   UnauthorizedException                  → 401
///   ForbiddenException                     → 403
///   NotFoundException                      → 404
///   ConflictException                      → 409
///   BusinessRuleException                  → 422
///   CsrfException                          → 400
///   ProviderException                      → 502
///   còn lại                                → 500
///
/// Bảo mật: KHÔNG lộ stack trace / message nội bộ của lỗi 500 ra client ở môi trường
/// production. Ở Development thì kèm chi tiết vào <c>details</c> để debug.
/// Mỗi response có traceId để đối chiếu với log.
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        var traceId = context.TraceIdentifier;

        // FluentValidation: gom từng field lỗi vào details[].
        if (ex is ValidationException valEx)
        {
            var details = valEx.Errors
                .Select(e => $"{e.PropertyName}: {e.ErrorMessage}")
                .ToArray();

            _logger.LogWarning("Validation failed ({Count} error(s)). TraceId={TraceId}", details.Length, traceId);
            await WriteResponseAsync(context, HttpStatusCode.BadRequest, "ValidationError",
                "One or more validation errors occurred.", details, traceId);
            return;
        }

        var (statusCode, errorType) = ex switch
        {
            UnauthorizedException  => (HttpStatusCode.Unauthorized,        "UnauthorizedError"),
            ForbiddenException     => (HttpStatusCode.Forbidden,           "ForbiddenError"),
            NotFoundException      => (HttpStatusCode.NotFound,            "NotFoundError"),
            ConflictException      => (HttpStatusCode.Conflict,            "ConflictError"),
            BusinessRuleException  => (HttpStatusCode.UnprocessableEntity, "BusinessRuleError"),
            CsrfException          => (HttpStatusCode.BadRequest,          "CsrfError"),
            ProviderException      => (HttpStatusCode.BadGateway,          "ProviderError"),
            _                      => (HttpStatusCode.InternalServerError, "InternalError")
        };

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            // Stack trace chỉ ghi log, KHÔNG trả ra client.
            _logger.LogError(ex, "Unhandled exception. TraceId={TraceId}", traceId);

            // Production: message generic, không details. Development: kèm chi tiết để debug.
            var message = "An unexpected error occurred.";
            var details = _env.IsDevelopment()
                ? new[] { ex.GetType().Name, ex.Message }
                : Array.Empty<string>();

            await WriteResponseAsync(context, statusCode, errorType, message, details, traceId);
            return;
        }

        // Lỗi domain đã biết — message an toàn để hiển thị, log ở mức Warning.
        _logger.LogWarning(ex, "Handled domain exception {ErrorType}. TraceId={TraceId}", errorType, traceId);
        await WriteResponseAsync(context, statusCode, errorType, ex.Message, Array.Empty<string>(), traceId);
    }

    private async Task WriteResponseAsync(
        HttpContext context, HttpStatusCode statusCode, string error, string message, string[] details, string traceId)
    {
        // Nếu response đã bắt đầu gửi (đã ghi header/body) thì không thể ghi đè → chỉ log, không nuốt lỗi âm thầm.
        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                "Cannot write error response — response already started. ErrorType={ErrorType}, TraceId={TraceId}",
                error, traceId);
            return;
        }

        var body = new
        {
            error,
            message,
            details,
            traceId
        };

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
    }
}
