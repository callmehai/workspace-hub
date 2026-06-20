namespace WorkspaceHub.Application.OAuth.Core;

public class BuildAuthUrlRequest
{
    public string ClientId { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public Domain.Entities.Integration Integration { get; init; } = null!;
    public string ServiceType { get; init; } = string.Empty;

    public BuildAuthUrlRequest() { }

    public BuildAuthUrlRequest(string clientId, string redirectUri, string state, Domain.Entities.Integration integration, string serviceType)
    {
        ClientId = clientId;
        RedirectUri = redirectUri;
        State = state;
        Integration = integration;
        ServiceType = serviceType;
    }
}
