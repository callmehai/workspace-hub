namespace WorkspaceHub.Application.OAuth.Core;

/// <summary>
/// Kết quả sau khi hoàn tất OAuth callback — trả về controller để map thành HTTP response.
/// Mô hình B: 1 lần grant có thể tạo/refresh nhiều Connection (1 row mỗi service được cấp).
/// </summary>
public class CompleteConnectionResult
{
    public string IntegrationKey { get; init; } = string.Empty;
    public string ProviderAccountId { get; init; } = string.Empty;
    public IReadOnlyList<ConnectionResult> Connections { get; init; } = [];

    public CompleteConnectionResult() { }

    public CompleteConnectionResult(string integrationKey, string providerAccountId, IReadOnlyList<ConnectionResult> connections)
    {
        IntegrationKey = integrationKey;
        ProviderAccountId = providerAccountId;
        Connections = connections;
    }
}

public class ConnectionResult
{
    public Guid Id { get; init; }
    public string ServiceType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;

    public ConnectionResult() { }

    public ConnectionResult(Guid id, string serviceType, string status)
    {
        Id = id;
        ServiceType = serviceType;
        Status = status;
    }
}
