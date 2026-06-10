namespace WorkspaceHub.Application.DTOs.Connections;

public class SetCredentialsRequest
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}
