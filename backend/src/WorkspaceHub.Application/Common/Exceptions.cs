namespace WorkspaceHub.Application.Common;

using System.Net;

/// <summary>
/// Lỗi 404 — resource không tồn tại.
/// Middleware sẽ map sang HTTP 404 NotFound.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string entityName, object key)
        : base($"{entityName} with id '{key}' was not found.") { }
}

/// <summary>
/// Lỗi 403 — user có token nhưng không đủ quyền (vd: không phải Owner của Folder).
/// Middleware sẽ map sang HTTP 403 Forbidden.
/// </summary>
public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
    public ForbiddenException()
        : base("You do not have permission to perform this action.") { }
}

/// <summary>
/// Lỗi 409 — vi phạm unique constraint hoặc trùng lặp.
/// Middleware map sang HTTP 409 Conflict.
/// Khi <see cref="Payload"/> khác null, middleware ghi <b>payload làm body 409</b>
/// (vd. <c>DriveLinkRestrictConflict</c> cho Case 1 tắt link) — không bọc envelope
/// <c>{ error, message, ... }</c>, để FE parse đúng contract.
/// </summary>
public class ConflictException : Exception
{
    /// <summary>Body 409 tuỳ chọn (serialize trực tiếp). Null = envelope lỗi chuẩn.</summary>
    public object? Payload { get; }

    public ConflictException(string message) : base(message) { }

    public ConflictException(string message, object payload) : base(message)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }
}

/// <summary>
/// Lỗi 422 — vi phạm business rule (không phải validation input đơn thuần).
/// Middleware sẽ map sang HTTP 422 Unprocessable Entity.
/// </summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}

/// <summary>
/// Lỗi 401 — Không có quyền truy cập hoặc credentials không đúng.
/// Middleware sẽ map sang HTTP 401 Unauthorized.
/// </summary>
public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message) { }
}

/// <summary>
/// Lỗi 400 — Sai lệch State (CSRF) trong luồng OAuth.
/// Middleware sẽ map sang HTTP 400 Bad Request.
/// </summary>
public class CsrfException : Exception
{
    public CsrfException(string message) : base(message) { }
}

/// <summary>
/// Lỗi 502 — Provider (Google) trả về lỗi không phải expired/not-found.
/// Ví dụ: 403 thiếu scope, 429 rate limit, 5xx server error.
/// Middleware sẽ map sang HTTP 502 Bad Gateway.
/// </summary>
public class ProviderException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public ProviderException(string message) : base(message) { }

    public ProviderException(string message, Exception inner) : base(message, inner) { }

    public ProviderException(string message, HttpStatusCode statusCode, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
