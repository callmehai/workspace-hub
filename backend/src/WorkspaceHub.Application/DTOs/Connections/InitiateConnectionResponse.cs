namespace WorkspaceHub.Application.DTOs.Connections;

public class InitiateConnectionResponse
{
    public string AuthorizationUrl { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}
