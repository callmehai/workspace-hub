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
