using System.Net;
using System.Text.Json;
using FluentValidation;
using WorkspaceHub.Application.Common;

namespace WorkspaceHub.Api.Middleware;

/// <summary>
/// Middleware tập trung bắt exception → trả error format chuẩn + traceId.
/// Mapping: NotFoundException→404, ForbiddenException→403, ConflictException→409,
///          BusinessRuleException→422, còn lại→500.
/// Xem API.md → Error format.
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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
        if (ex is ValidationException valEx)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;

            var details = valEx.Errors.Select(e => new
            {
                field = e.PropertyName,
                issue = e.ErrorMessage
            });

            var bodyObj = new
            {
                error = "ValidationError",
                message = "One or more validation errors occurred",
                details,
                traceId = context.TraceIdentifier
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(bodyObj, JsonOptions));
            return;
        }

        var (statusCode, errorType) = ex switch
        {
            NotFoundException      => (HttpStatusCode.NotFound,            "NotFoundError"),
            ForbiddenException     => (HttpStatusCode.Forbidden,           "AuthorizationError"),
            ConflictException      => (HttpStatusCode.Conflict,            "ConflictError"),
            BusinessRuleException  => (HttpStatusCode.UnprocessableEntity, "BusinessRuleError"),
            UnauthorizedException  => (HttpStatusCode.Unauthorized,        "UnauthorizedError"),
            CsrfException          => (HttpStatusCode.BadRequest,          "CsrfError"),
            _                      => (HttpStatusCode.InternalServerError, "InternalError")
        };

        // Log — stack trace chỉ ghi log, KHÔNG trả ra client (bảo mật).
        if (statusCode == HttpStatusCode.InternalServerError)
            _logger.LogError(ex, "Unhandled exception");
        else
            _logger.LogWarning(ex, "Handled domain exception: {ErrorType}", errorType);

        var traceId = context.TraceIdentifier;
        var body = new
        {
            error = errorType,
            message = statusCode == HttpStatusCode.InternalServerError
                ? "An unexpected error occurred."   // Không lộ message nội bộ ra client
                : ex.Message,
            traceId
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
    }
}
