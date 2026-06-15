namespace WorkspaceHub.Application.OAuth.Core;

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
