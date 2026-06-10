namespace WorkspaceHub.Application.Common;

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
/// Middleware sẽ map sang HTTP 409 Conflict.
/// </summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

/// <summary>
/// Lỗi 422 — vi phạm business rule (không phải validation input đơn thuần).
/// Middleware sẽ map sang HTTP 422 Unprocessable Entity.
/// </summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}
