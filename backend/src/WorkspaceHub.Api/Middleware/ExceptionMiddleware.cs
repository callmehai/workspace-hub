using System.Text.Json;
using WorkspaceHub.Application.Common;

namespace WorkspaceHub.Api.Middleware;

/// <summary>
/// Global exception handler — map domain exception → HTTP status.
/// NotFoundException → 404, BusinessRuleException → 422, mọi thứ khác → 500.
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (NotFoundException ex)
        {
            await WriteJson(ctx, StatusCodes.Status404NotFound, "NotFoundError", ex.Message);
        }
        catch (BusinessRuleException ex)
        {
            await WriteJson(ctx, StatusCodes.Status422UnprocessableEntity, "BusinessRuleError", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await WriteJson(ctx, StatusCodes.Status500InternalServerError, "InternalServerError", "Đã có lỗi xảy ra");
        }
    }

    private static Task WriteJson(HttpContext ctx, int statusCode, string error, string message)
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json";
        var body = JsonSerializer.Serialize(new { error, message });
        return ctx.Response.WriteAsync(body);
    }
}
