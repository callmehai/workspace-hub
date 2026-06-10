namespace WorkspaceHub.Application.Common;

/// <summary>Ném khi không tìm thấy resource theo ID/Key. Controller map → 404.</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

/// <summary>Ném khi vi phạm business rule (disabled, quota, ...). Controller map → 422.</summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}

/// <summary>Ném khi CSRF state không hợp lệ hoặc hết hạn. Controller map → 400.</summary>
public class CsrfException : Exception
{
    public CsrfException(string message) : base(message) { }
}

/// <summary>Ném khi resource đã tồn tại (duplicate). Controller map → 409.</summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}
