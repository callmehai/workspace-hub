using System.Text.Json;
using FluentValidation;
using WorkspaceHub.Application.Common;

namespace WorkspaceHub.Api.Middleware;

/// <summary>
/// Global exception handler — map domain exception → HTTP status.
/// RFC 7807-like error response with traceId.
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
        catch (ConflictException ex)
        {
            await WriteJson(ctx, StatusCodes.Status409Conflict, "ConflictError", ex.Message);
        }
        catch (UnauthorizedException ex)
        {
            await WriteJson(ctx, StatusCodes.Status401Unauthorized, "AuthenticationError", ex.Message);
        }
        catch (NotFoundException ex)
        {
            await WriteJson(ctx, StatusCodes.Status404NotFound, "NotFoundError", ex.Message);
        }
        catch (BusinessRuleException ex)
        {
            await WriteJson(ctx, StatusCodes.Status422UnprocessableEntity, "BusinessRuleError", ex.Message);
        }
        catch (CsrfException ex)
        {
            await WriteJson(ctx, StatusCodes.Status400BadRequest, "ValidationError", ex.Message);
        }
        catch (ValidationException ex)
        {
            await WriteValidationJson(ctx, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await WriteJson(ctx, StatusCodes.Status500InternalServerError, "InternalError", "An unexpected error occurred");
        }
    }

    private static Task WriteJson(HttpContext ctx, int statusCode, string error, string message)
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json";

        var body = JsonSerializer.Serialize(new
        {
            error,
            message,
            traceId = ctx.TraceIdentifier
        }, JsonOpts);

        return ctx.Response.WriteAsync(body);
    }

    private static Task WriteValidationJson(HttpContext ctx, ValidationException ex)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        ctx.Response.ContentType = "application/json";

        var details = ex.Errors.Select(e => new
        {
            field = e.PropertyName,
            issue = e.ErrorMessage
        });

        var body = JsonSerializer.Serialize(new
        {
            error = "ValidationError",
            message = "One or more validation errors occurred",
            details,
            traceId = ctx.TraceIdentifier
        }, JsonOpts);

        return ctx.Response.WriteAsync(body);
    }
}
