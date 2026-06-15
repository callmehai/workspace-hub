namespace WorkspaceHub.Application.OAuth.Core;

public class ExchangeCodeRequest
{
    public string Code { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = string.Empty;
    public Domain.Entities.Integration Integration { get; init; } = null!;
    public string ServiceType { get; init; } = string.Empty;

    public ExchangeCodeRequest() { }

    public ExchangeCodeRequest(string code, string clientId, string clientSecret, string redirectUri, Domain.Entities.Integration integration, string serviceType)
    {
        Code = code;
        ClientId = clientId;
        ClientSecret = clientSecret;
        RedirectUri = redirectUri;
        Integration = integration;
        ServiceType = serviceType;
    }
}
