namespace WorkspaceHub.Application.DTOs.Connections;

public class InitiateConnectionRequest
{
    public string IntegrationKey { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
}
